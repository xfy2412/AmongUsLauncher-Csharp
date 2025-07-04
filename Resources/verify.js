const express = require('express');
const redis = require('redis');
const axios = require('axios');
const path = require('path');
const app = express();
const port = 8001;

// Redis 客户端
const redisClient = redis.createClient({
  url: 'redis://localhost:6379'
});
redisClient.connect().catch(console.error);

// 中间件
app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use(express.static(path.join(__dirname, 'public')));

// 频率限制中间件（3 次/秒）
app.use(async (req, res, next) => {
// 跳过静态文件请求
  if (req.path.startsWith('/background.jpg') || req.path.endsWith('.html')) {
    return next();
  }
  const ip = req.headers['x-forwarded-for'] || req.socket.remoteAddress;
  const key = `rate_limit:${ip}`;
  const blacklistKey = `blacklist:${ip}`;

  // 检查黑名单
  const blacklisted = await redisClient.get(blacklistKey);
  if (blacklisted) {
    const ttl = await redisClient.ttl(blacklistKey);
    return res.redirect('/blacklist.html');
  }

  // 频率限制
  const count = await redisClient.incr(key);
  if (count === 1) {
    await redisClient.set(key, count, { EX: 1 }); // 1 秒窗口
  }
  if (count > 3) {
    const warnKey = `warn:${ip}`;
    let warnCount = await redisClient.incr(warnKey);
    if (warnCount === 1) {
      await redisClient.set(warnKey, warnCount, { EX: 60 }); // 警告 1 分钟有效
    }
    if (warnCount >= 3) {
      await redisClient.set(blacklistKey, '1', { EX: 300 }); // 拉黑 5 分钟
      await redisClient.del(warnKey);
      await redisClient.del(`whitelist:${ip}`);
      await redisClient.del(`download_access:${ip}`);
      return res.status(403).json({ message: '访问速度请不要超过3条/秒', warn: warnCount });
    } else {
      return res.status(429).json({ message: '访问速度请不要超过3条/秒', warn: warnCount });
    }
  }
  next();
});

// Cloudflare Turnstile 密钥
const TURNSTILE_SECRET_KEY = '0x4AAAAAABi887m0VT2PMXoXANP6c_I345k'; // 替换为实际 Secret Key

// 检查 IP 是否在白名单
async function isWhitelisted(ip) {
  const whitelist = await redisClient.get(`whitelist:${ip}`);
  if (whitelist) {
    await redisClient.set(`last_access:${ip}`, Date.now(), { EX: 3 * 24 * 60 * 60 });
    return true;
  }
  return false;
}

// 检查 IP 是否在黑名单
async function isBlacklisted(ip) {
  const blacklist = await redisClient.get(`blacklist:${ip}`);
  if (blacklist) {
    const ttl = await redisClient.ttl(`blacklist:${ip}`);
    return { blacklisted: true, ttl };
  }
  return { blacklisted: false };
}

// 验证页面
app.get('/', async (req, res) => {
  const ip = req.headers['x-forwarded-for'] || req.socket.remoteAddress;
  const blacklisted = await isBlacklisted(ip);

  if (blacklisted.blacklisted) {
    return res.sendFile(path.join(__dirname, 'public', 'blacklist.html'));
  }

  if (await isWhitelisted(ip)) {
    return res.sendFile(path.join(__dirname, 'public', 'index.html'));
  } else {
    return res.sendFile(path.join(__dirname, 'public', 'verify.html'));
  }
});

// Turnstile 验证接口
app.post('/verify', async (req, res) => {
  const ip = req.headers['x-forwarded-for'] || req.socket.remoteAddress;
  const token = req.body['cf-turnstile-response'];

  // 验证 Turnstile
  const response = await axios.post('https://challenges.cloudflare.com/turnstile/v0/siteverify', {
    secret: TURNSTILE_SECRET_KEY,
    response: token,
    remoteip: ip
  });

  if (!response.data.success) {
    return res.status(403).json({ message: '人机验证失败' });
  }

  // 检查是否已在白名单
  const isAlreadyWhitelisted = await isWhitelisted(ip);
  if (!isAlreadyWhitelisted) {
    await redisClient.set(`whitelist:${ip}`, '1', { EX: 3 * 24 * 60 * 60 });
  }

  // 设置 5 分钟下载权限
  await redisClient.set(`download_access:${ip}`, Date.now(), { EX: 5 * 60 });

  res.json({ message: '验证成功', downloadAccess: true });
});

// 获取剩余下载时间
app.get('/remaining-time', async (req, res) => {
  const ip = req.headers['x-forwarded-for'] || req.socket.remoteAddress;
  const accessTime = await redisClient.get(`download_access:${ip}`);
  if (!accessTime) {
    return res.json({ remaining: 0 });
  }
  const remaining = Math.max(0, 5 * 60 * 1000 - (Date.now() - parseInt(accessTime)));
  res.json({ remaining: Math.floor(remaining / 1000) });
});

// 获取黑名单剩余时间
app.get('/blacklist-time', async (req, res) => {
  const ip = req.headers['x-forwarded-for'] || req.socket.remoteAddress;
  const ttl = await redisClient.ttl(`blacklist:${ip}`);
  res.json({ ttl: Math.max(0, ttl) });
});

// 定时任务：清理 3 天未访问的 IP
setInterval(async () => {
  const keys = await redisClient.keys('last_access:*');
  for (const key of keys) {
    const lastAccess = await redisClient.get(key);
    if (Date.now() - parseInt(lastAccess) > 3 * 24 * 60 * 60 * 1000) {
      const ip = key.split(':')[1];
      await redisClient.del(`whitelist:${ip}`);
      await redisClient.del(`last_access:${ip}`);
    }
  }
}, 24 * 60 * 60 * 1000);

app.listen(port, () => {
  console.log(`Verify server running at http://localhost:${port}`);
});
