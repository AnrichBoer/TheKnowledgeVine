module.exports = {
  '/api/**': {
    target: 'http://127.0.0.1:5222',
    secure: false,
    changeOrigin: true,
    pathRewrite: { '^/api': '' },
  },
};
