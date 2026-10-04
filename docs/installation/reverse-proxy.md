# 域名、HTTPS 与反向代理

Docker 部署的 Compose 没有发布平台端口。外部反向代理连接运维管理的 `noctf-proxy`，通过容器网络访问服务。题目 Runtime 的直连端口是另一条网络路径。

## 访问路径

| 访问对象 | 公开入口 | 内部上游 |
| --- | --- | --- |
| 页面、API、SignalR | `https://noctf.example.com` | `noctf-web:8080` |
| 题目镜像 Registry | `https://registry.example.com` | `noctf-registry:5000` |
| Local 文件 | 平台授权下载入口 | Host 的 `/app/uploads` |
| 可选 RustFS | 配置的文件 HTTPS 域名 | 按 S3 overlay / Gateway 配置 |
| Docker 题目服务 | `challenges.example.com:实际随机端口` | 题目服务自己的 Docker port mapping |

已有面板反代时将 upstream 和证书配置到该面板；自建代理应运行在能解析容器 alias 的网络内。宿主上的 Nginx 不能天然解析 Docker alias，必须由运维另行设计网络；本文示例针对容器化 Nginx。

## 平台 Nginx 配置

仓库提供 [平台 Nginx 模板](https://github.com/D1no209/NoCTF/blob/HEAD/deploy/docker/nginx/noctf.conf)。先将 `server_name`、证书文件和网络替换为真实值。`map` 和 `log_format` 位于 Nginx `http` 上下文，不能直接粘进单个 `location`。

### 全新服务器准备独立代理

没有现成反代时，可以单独运行 Nginx，不依赖管理面板：

1. 创建/核验 `noctf-proxy`，并记录其真实 CIDR。
2. 创建 `/opt/noctf-proxy/conf.d` 与 `/opt/noctf-proxy/certs`。
3. 从你的 CA/证书管理流程取得平台、Registry 的完整证书链与私钥，分别存放子目录。例如 `certs/noctf/fullchain.pem`、`certs/noctf/privkey.pem`，Registry 使用 `certs/registry/`。只有自签证书不能作为正常公网 HTTPS 验收。
4. 复制平台 Nginx 模板到 `conf.d/noctf.conf`，将证书路径改为 `/etc/nginx/certs/noctf/...`，替换域名；创建包含 Registry TLS server 的 `conf.d/registry.conf`。
5. 启动 core，使 `noctf-web` 和 `noctf-registry` alias 存在。
6. 使用审核过的 Nginx 镜像先校验，再启动代理。

目录准备在服务器上执行：

```bash
sudo install -d -m 0755 /opt/noctf-proxy/conf.d
sudo install -d -m 0700 /opt/noctf-proxy/certs
sudo cp /opt/noctf-release/deploy/docker/nginx/noctf.conf /opt/noctf-proxy/conf.d/noctf.conf
```

用本地编辑器修改配置并安装证书后，再执行；`YOUR_NGINX_IMAGE_AT_DIGEST` 替换为你审核过、与配置兼容的官方 Nginx 完整镜像：

```bash
docker --context YOUR_CONTEXT run --rm --network noctf-proxy \
  --mount type=bind,source=/opt/noctf-proxy/conf.d,target=/etc/nginx/conf.d,readonly \
  --mount type=bind,source=/opt/noctf-proxy/certs,target=/etc/nginx/certs,readonly \
  YOUR_NGINX_IMAGE_AT_DIGEST nginx -t

docker --context YOUR_CONTEXT run -d --name noctf-edge --restart unless-stopped \
  --network noctf-proxy -p 80:80 -p 443:443 \
  --mount type=bind,source=/opt/noctf-proxy/conf.d,target=/etc/nginx/conf.d,readonly \
  --mount type=bind,source=/opt/noctf-proxy/certs,target=/etc/nginx/certs,readonly \
  YOUR_NGINX_IMAGE_AT_DIGEST
```

只有该入口代理发布 80/443，平台核心 Compose 保持内部端口。已有监听 80/443 的代理时复用它，不重复绑定。制定证书自动续期流程，更新后先校验再 reload；如果用了 ACME HTTP-01，还需在 TLS 跳转前配置对应 challenge location。

平台 location 的关键配置为：

```nginx
location / {
    proxy_pass http://noctf-web:8080;
    client_max_body_size 0;
    proxy_request_buffering off;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto https;
    proxy_set_header Accept-Encoding $http_accept_encoding;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $connection_upgrade;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
}
```

`$connection_upgrade` 由模板顶部的 `map` 定义。这里假设 Nginx 是唯一公网可信边缘，它丢弃客户端提供的 forwarded chain。存在 CDN、多级代理时需重新核对每一跳可信来源，不能无条件信任用户提交的 Header。

流式上传由 Endpoint 控制业务大小上限，代理不要提前缓冲大型修复包。TLS 握手、WebSocket Upgrade 和长连接超时都要验收。HTTP 入口可 308 跳转到 HTTPS；TLS 证书由运维申请并续期，配置向导不会安装或 reload Nginx。

在你自己的代理容器中检查并重载：

```bash
docker --context YOUR_CONTEXT exec YOUR_PROXY_CONTAINER nginx -t
docker --context YOUR_CONTEXT exec YOUR_PROXY_CONTAINER nginx -s reload
```

执行前已挂载或更新正确的代理配置。检查失败时保留旧配置，不直接 reload。

## Registry HTTPS

Registry 使用独立域名与证书，将 `/` 反代到 `http://noctf-registry:5000`，配置 HTTP/1.1、Host、`X-Forwarded-Proto https`、流式上传和足够的 push 超时。可在实际 TLS server 中使用：

```nginx
location / {
    proxy_pass http://noctf-registry:5000;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto https;
    proxy_request_buffering off;
    client_max_body_size 0;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
}
```

匿名访问 `/v2/` 返回 401 是已启用认证的正常结果；用向导的 Registry 账号通过 Docker 交互登录，再验证 push/pull：

```bash
curl -I https://registry.example.com/v2/
docker login registry.example.com
```

题目镜像填写宿主可解析的 HTTPS Registry 主机名，不写仅容器网络可见的 `registry:5000`。Host 使用安装目录中的 Docker auth 拉取镜像，部署账号的个人 `docker login` 不自动等于 Host 的凭据正确。

## .NET 信任与 Origin

安装配置必须匹配真实入口：

```dotenv
ForwardedHeaders__KnownNetworks__0=实际代理CIDR
ForwardedHeaders__AllowedHosts__0=noctf.example.com
Authentication__RefreshAllowedOrigins__0=https://noctf.example.com
Cors__AllowedOrigins__0=https://noctf.example.com
Webhooks__PublicBaseUrl=https://noctf.example.com
HumanVerification__Validation__PublicOrigin=https://noctf.example.com
```

Origin 精确匹配 scheme、host 和端口，不含路径、不用 `*`。刷新 Cookie 使用 Secure/HttpOnly，生产通过 HTTPS 访问。域名变更时同时更新 Refresh/CORS、SSO 回调、Cap/Turnstile、Webhook 和文件公开地址，并重启必要角色。

## 题目公网端口

Docker 对已声明的题目 TCP 端口请求 host port `0`，分配随机宿主端口。使用页面返回的实际访问地址，并保证 DNS、路由、NAT、安全组与宿主防火墙都能到达该端口。重置 Runtime 后端口和 UUID 都可能变化。

不要为 Docker Runtime 增加 HAProxy/Ingress 代理层，不在题库额外维护第二份公开端口表。普通平台反代不能替代 Runtime 直连验收。

## 代理验收

1. 浏览器证书可信，HTTP 正确跳转 HTTPS。
2. `/health/ready` 返回 200，管理员可登录。
3. 刷新页面和等待会话刷新后仍保持登录。
4. 比赛页面 WebSocket 能建立，提交结果和排行榜自动更新。
5. 附件上传与下载正常，大文件不被代理返回 413。
6. Docker 能 push/pull 题目镜像，公网客户端能访问题目随机端口。
7. 代理访问日志使用仓库的去查询串格式，不记录 SSO code/ticket。
