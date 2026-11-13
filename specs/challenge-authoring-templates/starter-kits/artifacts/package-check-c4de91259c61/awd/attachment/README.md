# 记录服务协议

`GET /health` 返回 `ok`。`PUT /flags/{storeId}/{flagId}` 保存原始字节，GET 同一路径读取。StoreId / FlagId 使用不带连字符的小写 UUID。有效攻击定位信息由赛事平台提供。

历史保存与接收窗口按本场公开规则执行，数据丢失后的责任按平台证据裁定。
