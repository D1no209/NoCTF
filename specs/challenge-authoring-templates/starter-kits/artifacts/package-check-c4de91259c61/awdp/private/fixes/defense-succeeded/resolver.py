def resolve(root, name):
    path = (root / name).resolve()
    return path if path.is_relative_to(root.resolve()) and path.is_file() else None
