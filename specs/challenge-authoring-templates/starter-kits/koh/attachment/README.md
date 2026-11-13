# 控制记录协议

POST `/claim` 写入 UTF-8 原文，成功返回 204。GET `/control` 返回当前值。内容为平台页面给出的本队 Control Flag，保留大小写与原始字节，不包装 JSON、不附加换行。

示例使用 `curl --data-binary @control-flag.txt http://HOST:PORT/claim`。地址以平台显示为准。
