#!/usr/bin/env bash
# Interactive Linux/WSL configuration. User files are data, never sourced.
set -Eeuo pipefail
set +x
umask 077
deploy_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
helper="$deploy_root/shared/configuration.py"
if [[ ${1:-} == --help ]]; then
    echo 'Usage: bash deploy/configure.sh'
    echo 'Interactive Docker or existing Linux Kubernetes configuration; deployment is opt-in.'
    exit 0
fi
[[ $# == 0 ]] || { echo 'Unknown argument; use --help.' >&2; exit 2; }
for command_name in python3 openssl; do
    command -v "$command_name" >/dev/null || { echo "Missing dependency: $command_name" >&2; exit 2; }
done
[[ -t 0 ]] || { echo 'This wizard requires a terminal; configuration data is not executable shell input.' >&2; exit 2; }
declare -A values=()
directory=''
cancelled() { echo; echo 'Cancelled. Existing configuration and services were retained.' >&2; exit 130; }
trap cancelled INT TERM
trap 'echo "Configuration failed; secret values are not printed. Existing services were not pruned." >&2' ERR

ask() {
    local key=$1 title=$2 fallback=${3:-} answer
    if [[ -v values[$key] ]]; then fallback=${values[$key]}
    elif [[ -n "$directory" ]]; then fallback=$(python3 "$helper" get "$directory" "$key"); fi
    [[ -n "$fallback" ]] || fallback=${3:-}
    printf '%s [%s]: ' "$title" "$fallback"
    IFS= read -r answer || cancelled
    values["$key"]=${answer:-$fallback}
}
secret() {
    local key=$1 title=$2 existing='' answer
    if [[ -v values[$key] ]]; then existing=${values[$key]}
    elif [[ -n "$directory" ]]; then existing=$(python3 "$helper" get "$directory" "$key"); fi
    printf '%s（输入不回显；回车%s）: ' "$title" "$([[ -n "$existing" ]] && echo 保留 || echo 自动生成)"
    IFS= read -rs answer || cancelled
    echo
    if [[ -n "$answer" ]]; then values["$key"]=$answer
    elif [[ -n "$existing" ]]; then values["$key"]=$existing
    elif [[ "$key" == EMAIL_VERIFICATION_ENCRYPTION_KEY ]]; then values["$key"]=$(openssl rand -base64 32)
    else values["$key"]=$(openssl rand -hex 32)
    fi
}
echo 'NoCTF 配置向导：生成配置不会更改 CNI、Docker daemon 或外部反向代理。'
printf '部署方式：1 Docker（默认） / 2 已有 Linux Kubernetes: '
IFS= read -r choice || cancelled
case ${choice:-1} in 1) values[target]=docker ;; 2) values[target]=kubernetes ;; *) echo 'Invalid selection.' >&2; exit 2 ;; esac
printf '安装目录 [/opt/noctf]: '
IFS= read -r directory || cancelled
directory=${directory:-/opt/noctf}
values[directory]=$directory
if [[ -f "$directory/installation.json" || -f "$directory/.env" ]]; then
    echo '找到已有配置：默认保留密钥与数据，修改前将保存受限权限配置备份。'
else
    echo '首次配置：生成独立密钥与目录；不会导入或替换现有业务数据。'
fi
python3 -c 'import yaml' 2>/dev/null || { echo 'Missing dependency: python3-yaml (install it before configuring).' >&2; exit 2; }
if [[ ${values[target]} == docker ]]; then
    for command_name in docker htpasswd; do command -v "$command_name" >/dev/null || { echo "Missing dependency: $command_name" >&2; exit 2; }; done
    docker context ls --format '{{.Name}}'
    ask context 'Docker context' "$(docker context show)"
else
    command -v kubectl >/dev/null || { echo 'Missing dependency: kubectl' >&2; exit 2; }
    kubectl config get-contexts -o name
    ask context 'Kubernetes context' "$(kubectl config current-context)"
    if [[ ! -f "$directory/installation.json" ]]; then
        python3 "$helper" import-cluster "$directory" "${values[context]}"
    fi
fi
while true; do
    ask COMPOSE_PROJECT_NAME '项目名称' noctf
    ask NOCTF_PLATFORM_IMAGE '统一 Host 镜像（repository@sha256）'
    ask NOCTF_PUBLIC_HOST '平台公开域名' noctf.example.com
    ask NOCTF_PUBLIC_URL '平台 HTTPS Origin' "https://${values[NOCTF_PUBLIC_HOST]}"
    ask DOCKER_PUBLISHED_HOST '题目公开连接域名或 IPv4' challenges.example.com
    ask NOCTF_PROXY_NETWORK '可信代理的实际 Pod/bridge CIDR'
    ask SEED_ADMIN_USERNAME '管理员用户名' admin
    ask SEED_ADMIN_EMAIL '管理员邮箱'
    secret SEED_ADMIN_PASSWORD '管理员密码'
    for key in POSTGRES_PASSWORD JWT_SECRET RUNNER_SCORING_SECRET EMAIL_VERIFICATION_ENCRYPTION_KEY REGISTRY_HTTP_SECRET; do secret "$key" "$key"; done
    if [[ ${values[target]} == docker ]]; then
        ask storage '文件存储 local / s3（RustFS）' local
        ask REGISTRY_HOST 'Registry HTTPS 主机名' registry.example.com
        ask REGISTRY_USERNAME 'Registry 用户名' challenge-publisher
        secret REGISTRY_PASSWORD 'Registry 密码'
        ask monitoring '启用独立监控 yes / no' no
    else
        values[storage]=s3; values[monitoring]=yes
        detected_dns=$(kubectl --context "${values[context]}" -n kube-system get service kube-dns -o jsonpath='{.spec.clusterIP}')
        ask clusterDns '集群 DNS Service IP' "$detected_dns"
        ask clusterDomain '集群 DNS 域' cluster.local
        ask protectedCidrs '保护的集群/管理 CIDR（逗号分隔）' '10.0.0.0/8,172.16.0.0/12,192.168.0.0/16'
        ask storageClass 'Retain 存储类（需预先准备 PV 或 CSI）' noctf-retain
        ask tlsCert '覆盖平台/文件域名的 TLS 证书文件'
        ask tlsKey 'TLS 私钥文件'
        ask pullConfig '私有镜像 Docker config.json 文件（可留空）'
    fi
    if [[ ${values[storage]} == s3 ]]; then
        ask S3_PUBLIC_URL 'S3 HTTPS Origin' https://files.example.com
        ask S3_BUCKET 'S3 bucket' noctf
        secret S3_ACCESS_KEY 'S3 access key'
        secret S3_SECRET_KEY 'S3 secret key'
    fi
    secret GRAFANA_PASSWORD 'Grafana 管理密码'
    if [[ ${values[target]} == docker && ${values[monitoring]} == yes ]]; then
        ask GRAFANA_PUBLIC_HOST 'Grafana 公开域名' grafana.example.com
        ask GRAFANA_PUBLIC_URL 'Grafana HTTPS Origin' "https://${values[GRAFANA_PUBLIC_HOST]}"
        secret MONITOR_PASSWORD '只读 PostgreSQL exporter 密码'
    fi
    printf '\n目标=%s，context=%s，目录=%s\nHost 镜像=%s\n平台=%s，题目=%s，存储=%s\n' \
        "${values[target]}" "${values[context]}" "$directory" "${values[NOCTF_PLATFORM_IMAGE]}" \
        "${values[NOCTF_PUBLIC_URL]}" "${values[DOCKER_PUBLISHED_HOST]}" "${values[storage]}"
    echo '密钥不显示。外部网络、TLS 代理、数据迁移由运维管理。'
    printf '1 生成并校验（默认） / 2 重新填写 / 3 取消: '
    IFS= read -r choice || cancelled
    case ${choice:-1} in 1) break ;; 2) continue ;; 3) cancelled ;; *) echo 'Invalid selection.' >&2; exit 2 ;; esac
done
for key in "${!values[@]}"; do printf '%s\0%s\0' "$key" "${values[$key]}"; done | python3 "$helper" generate "$directory"
printf '是否部署到以上 context？输入 deploy 才执行，回车仅保留配置: '
IFS= read -r choice || cancelled
if [[ "$choice" == deploy ]]; then
    python3 "$helper" deploy "$directory"
else
    echo '已生成配置。后续先复核外部反代/网络，再执行：'
    printf 'python3 %q deploy %q\n' "$helper" "$directory"
fi
