def resolve(root, name):
    # 选手在此实现文件访问规则，并保留公开目录的正常读取。
    path = (root / name).resolve()
    return path if path.is_file() else None
