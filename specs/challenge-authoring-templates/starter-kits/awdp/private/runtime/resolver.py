def resolve(root, name):
    # 教学漏洞：公开文件解析缺少目录边界检查。
    path = (root / name).resolve()
    return path if path.is_file() else None
