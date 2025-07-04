const express = require('express');
const crypto = require('crypto');
const fs = require('fs').promises;
const redis = require('redis');
const app = express();
const port = 8000;

// 123 云盘配置
const UID = 1813732458;
const PRIMARY_KEY = 'abbnsygaul114514';
const CONFIG_PATH = './config.json';

// 读取 JSON 配置文件
async function loadConfig() {
  try {
    const data = await fs.readFile(CONFIG_PATH, 'utf8');
    return JSON.parse(data);
  } catch (error) {
    throw new Error('Failed to load or parse config file');
  }
}

// 验证请求合法性
function validateRequest(config, type, name, version) {
  if (!config.type[type]) {
    throw new Error(`Invalid type: ${type}`);
  }
  if (type === 'game' || type === 'bepinex') {
    if (!config.type[type].includes(version)) {
      throw new Error(`Invalid version for ${type}: ${version}`);
    }
  } else if (type === 'mod') {
    if (!name) {
      throw new Error('Name is required for type=mod');
    }
    if (!config.type.mod[name]) {
      throw new Error(`Invalid name for mod: ${name}`);
    }
    if (!config.type.mod[name].versions[version]) {
      throw new Error(`Invalid version for mod ${name}: ${version}`);
    }
  }
}

// 生成 123 云盘鉴权 URL
function generate123PanSignedUrl(uid, filePath, primaryKey, expireSeconds = 60) {
  const timestamp = Math.floor(Date.now() / 1000) + expireSeconds;
  const rand = Math.floor(Math.random() * 1000);
  const fullPath = `/${uid}/${filePath}`;
  const signString = `${fullPath}-${timestamp}-${rand}-${uid}-${primaryKey}`;
  const md5Hash = crypto.createHash('md5').update(signString).digest('hex');
  const authKey = `${timestamp}-${rand}-${uid}-${md5Hash}`;
  return `https://vip.123pan.cn/${uid}/${filePath}?auth_key=${authKey}`;
}

// 下载接口
app.get('/api/download', async (req, res) => {
  try {

    const { type, name, version } = req.query;

    // 验证必需参数
    if (!type || !version) {
      return res.status(400).json({ error: 'Type and version are required' });
    }

    // 加载配置文件
    const config = await loadConfig();

    // 验证请求合法性
    validateRequest(config, type, name, version);

    // 构造文件路径
    let filePath = `AUL_files/${type}`;
    if (type === 'mod') {
      if (!name) {
        return res.status(400).json({ error: 'Name is required for type=mod' });
      }
      filePath += `/${name}`;
    }
    filePath += `/${version}.zip`;

    // 生成鉴权 URL
    const signedUrl = generate123PanSignedUrl(UID, filePath, PRIMARY_KEY);

    // 返回响应
    res.json({
      url: signedUrl,
      expires_at: Math.floor(Date.now() / 1000) + 60
    });
  } catch (error) {
    res.status(400).json({ error: error.message });
  }
});

// 启动服务器
app.listen(port, () => {
  console.log(`Server running at http://localhost:${port}`);
});
