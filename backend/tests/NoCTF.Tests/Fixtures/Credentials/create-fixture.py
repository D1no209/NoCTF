from pathlib import Path

from openpyxl import Workbook

workbook = Workbook()
sheet = workbook.active
sheet.title = "选手名单"
sheet.append(["用户名", "邮箱", "队伍名称", "是否队长", "赛道", "姓名", "学号", "学校"])
sheet.append(["alice", None, "蓝队", True, None, "张三", "001234", "成都理工大学"])
sheet.append(["bob", None, "蓝队", False, None, None, None, None])
sheet["G2"].number_format = "@"
workbook.save(Path(__file__).with_name("participants.xlsx"))
