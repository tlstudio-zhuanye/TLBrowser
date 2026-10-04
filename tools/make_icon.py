"""从 TLSTUDIO 母版 logo 生成浏览器用的多尺寸 app.ico。

母版：C:\\Users\\Administrator\\WorkBuddy\\2026-10-03-20-42-57\\logo_crop.png
（上图 TL 圆环标记 + 下图 TL STUDIO 文字；图标只取圆环标记）
"""
import os
import sys
from PIL import Image

SRC = r"C:\Users\Administrator\WorkBuddy\2026-10-03-20-42-57\logo_crop.png"
SRC_DOUBLER = r"C:\Users\Administrator\Desktop\logo_doubler.png"
OUT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(OUT_DIR, "src", "assets")

ICO = os.path.join(OUT_DIR, "src", "app.ico")
LOGO_PNG = os.path.join(ASSETS, "logo.png")
LOGO_DOUBLER = os.path.join(ASSETS, "logo_doubler.png")
PREVIEW = os.path.join(OUT_DIR, "_icon_preview.png")

SIZES = [256, 128, 64, 48, 32, 24, 16]


def key_white_to_alpha(img: Image.Image, keep_size: bool = False) -> Image.Image:
    """白底彩色标 → 透明底。按 a = 1 - min(r,g,b)/255 反预乘，抗锯齿边缘自动得到半透明。"""
    img = img.convert("RGBA")
    px = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, _ = px[x, y]
            lo = min(r, g, b)
            a = 1.0 - lo / 255.0
            if a <= 0.004:
                px[x, y] = (0, 0, 0, 0)
                continue
            nr = int(max(0.0, min(255.0, (r - (1 - a) * 255) / a)))
            ng = int(max(0.0, min(255.0, (g - (1 - a) * 255) / a)))
            nb = int(max(0.0, min(255.0, (b - (1 - a) * 255) / a)))
            px[x, y] = (nr, ng, nb, int(round(a * 255)))

    if not keep_size:
        box = img.getbbox()
        if box:
            img = img.crop(box)
    return img


def bbox_of_mark(img: Image.Image) -> tuple[int, int, int, int]:
    """只看图片上半部分，找到圆环标记的外接框（非白像素）。"""
    w, h = img.size
    top = img.crop((0, 0, w, int(h * 0.68))).convert("RGB")
    gray = top.convert("L")
    # 非白 = 有内容；阈值放宽一点，避免抗锯齿边缘被切
    mask = gray.point(lambda v: 255 if v < 235 else 0)
    box = mask.getbbox()
    if box is None:
        return (0, 0, w, h)
    return box


def main() -> int:
    if not os.path.exists(SRC):
        print("母版不存在:", SRC)
        return 1

    os.makedirs(ASSETS, exist_ok=True)
    img = Image.open(SRC).convert("RGBA")

    box = bbox_of_mark(img)
    mark = img.crop(box)

    # 正方形画布 + 留 6% 内边距，保证各尺寸下都不贴边
    mw, mh = mark.size
    side = int(max(mw, mh) * 1.12)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(mark, ((side - mw) // 2, (side - mh) // 2), mark)

    # 母版是白底 —— 把接近白的像素清成透明，图标才能贴合深色任务栏
    px = canvas.load()
    for y in range(side):
        for x in range(side):
            r, g, b, a = px[x, y]
            if a and r > 244 and g > 244 and b > 244:
                px[x, y] = (r, g, b, 0)

    canvas.save(ICO, format="ICO", sizes=[(s, s) for s in SIZES])
    canvas.resize((256, 256), Image.LANCZOS).save(PREVIEW)
    canvas.resize((128, 128), Image.LANCZOS).save(LOGO_PNG)

    print("app.ico  ->", ICO, os.path.getsize(ICO), "bytes")
    print("logo.png ->", LOGO_PNG, os.path.getsize(LOGO_PNG), "bytes")

    # 启动动画用的 TL DOUBLER STUDIO 横向标（白底转透明）
    if os.path.exists(SRC_DOUBLER):
        d = key_white_to_alpha(Image.open(SRC_DOUBLER))
        d.save(LOGO_DOUBLER)
        d.resize((d.width // 2, d.height // 2), Image.LANCZOS).save(
            os.path.join(OUT_DIR, "_doubler_preview.png"))
        print("logo_doubler.png ->", LOGO_DOUBLER, os.path.getsize(LOGO_DOUBLER),
              "size", d.size)
    else:
        print("!! 缺少 TL DOUBLER 母版:", SRC_DOUBLER)

    print("mark box ->", box, "canvas ->", canvas.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
