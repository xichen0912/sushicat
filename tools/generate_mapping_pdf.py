from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.platypus import SimpleDocTemplate, Paragraph, Table, TableStyle, Spacer, PageBreak, Flowable
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output/pdf/P2_Setup_and_Mapping.pdf'
OUT.parent.mkdir(parents=True, exist_ok=True)
pdfmetrics.registerFont(TTFont('CJK', '/System/Library/Fonts/STHeiti Light.ttc', subfontIndex=1))
teal = colors.HexColor('#163E45')
ink = colors.HexColor('#21383D')
muted = colors.HexColor('#586C71')
paper = colors.HexColor('#EDF4F3')
accent = colors.HexColor('#B6D9D1')
body = ParagraphStyle('body', fontName='CJK', fontSize=10.2, leading=15.8, textColor=ink, wordWrap='CJK', spaceAfter=7)
small = ParagraphStyle('small', parent=body, fontSize=9, leading=13, spaceAfter=0)
heading = ParagraphStyle('heading', parent=body, fontSize=15, leading=20, textColor=teal, spaceBefore=10, spaceAfter=8)
white = ParagraphStyle('white', parent=small, textColor=colors.white)


def p(text, style=body):
    return Paragraph(text, style)


class Formation(Flowable):
    def __init__(self):
        super().__init__()
        self.width = 523
        self.height = 148
    def draw(self):
        c = self.canv
        c.setFillColor(paper)
        c.roundRect(0, 0, self.width, self.height, 10, fill=1, stroke=0)
        c.setFillColor(muted)
        c.setFont('CJK', 9)
        c.drawString(14, 131, '非镜像摄像头预览中的位置；具体机位与距离需现场验证')
        for x, label, action in [(65, 'P1', '前腿 / 行走 / 转向'), (235, 'P2', '后腿 / 尾巴'), (405, 'P3', '头 / 鼻 / 耳')]:
            c.setFillColor(teal)
            c.roundRect(x-45, 79, 110, 36, 6, fill=1, stroke=0)
            c.setFillColor(colors.white)
            c.setFont('Helvetica-Bold', 12)
            c.drawCentredString(x+10, 93, label)
            c.setFillColor(ink)
            c.setFont('CJK', 9)
            c.drawCentredString(x+10, 64, action)
        c.setStrokeColor(accent)
        for x in [75, 245, 415]:
            c.line(x, 53, 261, 30)
        c.setFillColor(ink)
        c.setFont('CJK', 10)
        c.drawCentredString(261, 14, '全身可见、固定站位 → 摄像头 → 本地追踪 → Unity 大象')


def page(c, doc):
    w, h = A4
    c.setFillColor(teal)
    c.rect(0, h-90, w, 90, fill=1, stroke=0)
    c.setFillColor(accent)
    c.setFont('Helvetica', 9)
    c.drawString(36, h-28, 'DESCOMP PROJECT 2  /  SETUP & MAPPING')
    c.setFillColor(colors.white)
    c.setFont('CJK', 23)
    c.drawString(36, h-61, '三人共同驱动的大象')
    c.setFont('CJK', 9)
    c.setFillColor(muted)
    c.drawString(36, 24, '2026-10-07 · 搭建说明 / 初始方案 · 现场验证待完成')
    c.setFont('Helvetica', 9)
    c.drawRightString(w-36, 24, str(doc.page))


story = [p('这是已准备的工程方案，不是完成表演的实测报告。队名、本人和三位成员姓名请填写 team.json；排练后更新分工理由与参数。'),
         p('01  分工与动作转译', heading)]
rows = [[p(t, white) for t in ['ID', '身体动作', '大象控制', '中立 / 参数', '选择理由（待个人确认）']]]
data = [
    ['P1<br/>姓名待填', '左右抬脚；<br/>左手升降；<br/>躯干左右倾斜', '左右前腿；<br/>前后移动；<br/>转向及跑步', '脚着地、肩平稳、左手腰高。<br/>腿 -25°；灵敏度 1；关节平滑 8。', '把前肢、行走、转向交给同一人，便于共同节拍和路径判断。'],
    ['P2<br/>姓名待填', '左右抬脚；<br/>躯干左右倾斜', '左右后腿；<br/>尾巴摆动', '脚着地、身体直立。<br/>腿 -25°、尾 30°；灵敏度 1；平滑 8。', '与 P1 的口令同步，控制后肢节奏；尾部动作保持独立。'],
    ['P3<br/>姓名待填', '躯干倾斜；<br/>右手升降；<br/>双手展开收拢', '头部转向；<br/>鼻子卷曲；<br/>左右耳', '右手腰高、肩放松、手距舒适。<br/>头 25°；鼻 7 段各 12°；耳 +25° / -25°。', '集中控制头部与探查动作，便于在团队慢速移动时表达局部行为。']
]
rows.extend([[p(t, small) for t in row] for row in data])
table = Table(rows, colWidths=[46, 91, 88, 147, 151])
table.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),teal), ('VALIGN',(0,0),(-1,-1),'TOP'), ('LEFTPADDING',(0,0),(-1,-1),9), ('RIGHTPADDING',(0,0),(-1,-1),9), ('TOPPADDING',(0,0),(-1,-1),9), ('BOTTOMPADDING',(0,0),(-1,-1),9), ('ROWBACKGROUNDS',(0,1),(-1,-1),[paper,colors.white]), ('LINEBELOW',(0,0),(-1,-1),.5,accent)]))
story += [table, Spacer(1, 8), p('旋转正负号沿用 rig 的局部骨骼轴；左右按表演者身体区分。腿部动作在行走动画上叠加 0.35 权重，其他部位权重 1；人体动作并不逐帧复制大象四足步态。', small),
          p('02  固定站位与集体协调', heading), Formation(), Spacer(1, 9),
          p('正对相机，全身与双手入镜，不交换位置或互相遮挡。初始可从约 3-5 米试拍，按镜头调整。P1 发“准备、走、停”口令；P2 跟节拍；P3 在慢速时展示头鼻耳。身份错配时停止、Reassign people、重新校准。'), PageBreak(),
          p('03  校准与控制边界', heading),
          p('P3 课程选择 3 / Upper body / Camera，点击 Set neutral pose (10 seconds)，保持中立并等到 Neutral pose saved；拳交互需先安装 gesture_recognizer.task。人数、模式或身份分配变化后重新校准。每个人先单独展示，再合练。'),
          p('参考参数：最低置信度 0.5、失联阈值 0.5 秒；行走/转向灵敏度 1、死区 0.12、输入平滑 6。回到中立给出零偏移；任一队员失联即暂停移动；持续检测到失联达 0.5 秒后清除校准。即时停止可关闭 Enable locomotion 或退出 Play。参数效果尚待真人验证。'),
          p('04  保留的控制与新增任务', heading),
          p('保留三人部位分配、六个姿态信号、步态叠加、跟随相机和合屏录制。明确适配运行时脚本：多人丢失暂停、缺失动作诊断、遗漏槽位、自定义来源切换和上半身桥接启动参数。身份歧义/持续缺失后锁定，需重置与校准；三人起始人数须准确。首次创建的新团队场景把移动半径从 15 扩到 30 Unity 单位，覆盖固定障碍场；原参考场景保留。'),
          p('新增独立 MediaPipe ImageSegmenter：同一相机帧生成青色人体轮廓与 foreground 像素占比，目标采样上限 5 Hz。它不分配 P1-P3 身份，不控制大象。开启/关闭的同条件测试仍需现场完成；额外推理可能影响后续帧率。'),
          p('新增轨迹记录器：单独开始/停止，输出虚拟位置、速度、XZ 路程、地标停留与最近路径投影。日志位于 Logs/Simulation/&lt;UTC-id&gt;/，合屏 MP4 位于 Recordings/&lt;session&gt;/。路径投影不等于按顺序完成课程。'),
          p('05  验证状态与解释限度', heading),
          p('已验证：Unity 6000.6.3f1 / arm64、URP 17.6.0 导入与 C# 编译；正式场景及障碍场前备份已保存。学生无相机检查通过坐姿合成输入、失联回中、步态、转向与输入模式复位；Console 无新增错误。三份运行时修复已精确锁定，starter 原文件与教师编辑器工具保留。'),
          p('无相机 Play 试运行通过滑块行走、停止和复位；14.25 秒日志导出 142 行轨迹，明确标记 TestSliders / virtual_proxy_simulation。另已通过 100 项适配、11 项 starter tracking 和 8 项采集工具测试；三人合成回归通过独立控制、丢失停止、重校准、来源切换及遗漏槽位。124 项来源指纹、10 个导入材质与 3 份修复脚本精确核验。'),
          p('实际单人相机已通：两次约 40 秒诊断接收 429 / 589 个姿态包并实时预览；手部置信度不足、追踪间断，校准尚未通过。待验证：单人校准与录像、三人追踪恢复、障碍场尝试及成片播放。合成测试和滑块日志不是真人实验或人体分割准确率；反思中的改进需真实前后结果。详细证据见 Docs/SETUP_VERIFICATION.md。'),
          p('人类身体比例、关节和感知与大象不同；校准、平滑与动画也会引入变形。老师的地标没有碰撞器，因此日志不能声称测量了碰撞或真实大象反应。本工程是人类代理驱动的行为模拟。'),
          Spacer(1, 8), p('依据：TDF DesComp Project 2 Brief，第 6-10 页；DesComp10，第 31-34 页；studentstarter revision 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f。可编辑版本及详细操作见 Docs/MAPPING_中文.md 和 DIGIPHANT_SETUP.md。', small)]
doc = SimpleDocTemplate(str(OUT), pagesize=A4, rightMargin=36, leftMargin=36, topMargin=110, bottomMargin=43,
                        title='DigiPhant P2 - Setup and Mapping', author='Project setup draft')
doc.build(story, onFirstPage=page, onLaterPages=page)
print(OUT)
