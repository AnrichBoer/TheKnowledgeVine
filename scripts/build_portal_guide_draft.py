#!/usr/bin/env python3
"""Build the two-page layout proof; it is deliberately outside the public web assets.

This is a review draft, not the final parent guide. Real phone captures and an HTTPS
preview must be verified before the guide is finalized or enabled in welcome emails.
Run with the bundled Python runtime (reportlab, Pillow and pypdf).
"""
from pathlib import Path
from xml.sax.saxutils import escape

from PIL import Image
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output/pdf/knowledge-vine-phone-guide-draft.pdf"
GREEN = colors.HexColor("#184f36")
GOLD = colors.HexColor("#a38748")
CREAM = colors.HexColor("#fcfaf4")
PALE = colors.HexColor("#e8eee4")
INK = colors.HexColor("#294637")
MUTED = colors.HexColor("#5b6b60")
WIDTH, HEIGHT = A4
MARGIN = 40


def register_fonts():
    fonts = Path("/usr/share/fonts/truetype")
    for name, filename in [("Body", "dejavu/DejaVuSans.ttf"),
                           ("BodyBold", "dejavu/DejaVuSans-Bold.ttf"),
                           ("Display", "liberation/LiberationSerif-Regular.ttf")]:
        pdfmetrics.registerFont(TTFont(name, str(fonts / filename)))
    pdfmetrics.registerFontFamily("Body", normal="Body", bold="BodyBold")


def paragraph(c, text, x, top, width, size=10, leading=15, colour=INK, align=0):
    p = Paragraph(text, ParagraphStyle("body", fontName="Body", fontSize=size,
        leading=leading, textColor=colour, alignment=align))
    _, height = p.wrap(width, HEIGHT)
    p.drawOn(c, x, HEIGHT - top - height)
    return top + height


def card(c, x, top, width, height, fill=PALE):
    c.setFillColor(fill)
    c.roundRect(x, HEIGHT - top - height, width, height, 10, stroke=0, fill=1)


def step(c, number, title, body, top):
    c.setFillColor(GREEN)
    c.circle(MARGIN + 12, HEIGHT - top - 12, 12, stroke=0, fill=1)
    c.setFont("BodyBold", 11)
    c.setFillColor(colors.white)
    c.drawCentredString(MARGIN + 12, HEIGHT - top - 16, str(number))
    c.setFillColor(GREEN)
    c.setFont("BodyBold", 11)
    c.drawString(MARGIN + 36, HEIGHT - top - 12, title)
    return paragraph(c, body, MARGIN + 36, top + 22, 260, size=10, leading=15) + 16


def page(c, platform, page_number, steps, source, source_name):
    c.setFillColor(CREAM)
    c.rect(0, 0, WIDTH, HEIGHT, stroke=0, fill=1)
    c.setFillColor(GREEN)
    c.rect(0, HEIGHT - 27, WIDTH, 27, stroke=0, fill=1)
    c.setFillColor(colors.white)
    c.setFont("BodyBold", 8)
    c.drawCentredString(WIDTH / 2, HEIGHT - 17, "REVIEW DRAFT - NOT FOR PARENT DISTRIBUTION")
    c.drawImage(str(ROOT / "knowledevinefront/design/brand/knowledge-vine-horizontal.png"),
        MARGIN, HEIGHT - 109, 178, 65, preserveAspectRatio=True, mask="auto")
    c.setFillColor(GOLD)
    c.setFont("BodyBold", 8)
    c.drawRightString(WIDTH - MARGIN, HEIGHT - 77, "PARENT PORTAL / PHONE GUIDE")
    c.setStrokeColor(GOLD)
    c.setLineWidth(0.6)
    c.line(MARGIN, HEIGHT - 121, WIDTH - MARGIN, HEIGHT - 121)

    c.setFillColor(GREEN)
    c.setFont("Display", 31)
    c.drawString(MARGIN, HEIGHT - 160, "Your child's day, a tap away.")
    paragraph(c, f"<b>{escape(platform)}</b>  |  Add Knowledge Vine Portal to your home screen.",
        MARGIN, 174, WIDTH - 2 * MARGIN, size=10)

    card(c, MARGIN, 207, WIDTH - 2 * MARGIN, 76)
    paragraph(c, "<b>Before you start</b><br/>Use the school's welcome email to set your password, then sign in with the same email address. "
        "Setup links last 24 hours. You can also keep using the portal in your browser.",
        MARGIN + 14, 220, WIDTH - 2 * MARGIN - 28, size=9.4, leading=14)

    top = 310
    for i, (title, body) in enumerate(steps, 1):
        top = step(c, i, title, body, top)
    assert top < 675, "Installation steps overlap the lower cards."

    image_path = ROOT / "docs/portal-designs/pwa-step7-vm-install-phone.png"
    image_width = 151
    with Image.open(image_path) as im:
        image_height = image_width * im.height / im.width
    image_x = WIDTH - MARGIN - image_width
    c.setStrokeColor(colors.HexColor("#ced8c9"))
    c.setLineWidth(0.7)
    c.rect(image_x - 1, HEIGHT - 305 - image_height - 1, image_width + 2, image_height + 2, stroke=1, fill=0)
    c.drawImage(str(image_path), image_x, HEIGHT - 305 - image_height,
        image_width, image_height, mask="auto")
    paragraph(c, "Actual portal help at phone width.<br/>This is a desktop browser capture.<br/>Native phone captures are pending.",
        image_x, 305 + image_height + 9, image_width, size=7.4, leading=10.5, colour=MUTED, align=TA_CENTER)

    card(c, MARGIN, 683, WIDTH - 2 * MARGIN, 83)
    paragraph(c, "<b>A few useful things to know</b><br/>An internet connection is needed to read notes and save changes. "
        "Sessions end after 30 minutes of inactivity. Sign out on shared devices. "
        "Installing the app does not switch on push notifications.<br/>"
        'Need help? <link href="mailto:info@knowledgevinemontessori.co.za" color="#184f36">info@knowledgevinemontessori.co.za</link>',
        MARGIN + 14, 695, WIDTH - 2 * MARGIN - 28, size=9.1, leading=13.5)
    paragraph(c, f'<link href="{escape(source)}" color="#184f36">{escape(source_name)} installation reference</link>'
        " | Menu wording varies by phone/version.", MARGIN, 782, WIDTH - 2 * MARGIN, size=7.3, leading=10)
    c.setFillColor(MUTED)
    c.setFont("Body", 7.5)
    c.drawString(MARGIN, 24, "Layout proof / 15 October 2026 / HTTPS and real-phone verification pending")
    c.drawRightString(WIDTH - MARGIN, 24, f"{page_number} / 2")
    c.showPage()


def main():
    register_fonts()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(OUT), pagesize=A4, pageCompression=1)
    c.setTitle("Knowledge Vine phone installation guide - review draft")
    c.setAuthor("Knowledge Vine Montessori")
    c.setSubject("Review draft: actual iPhone/Android verification and screenshots are pending")
    page(c, "iPhone / Safari", 1, [
        ("Open the school portal", "Open the school's secure portal address in <b>Safari</b>. "
         "If your email app opens its own browser, open the address in Safari instead."),
        ("Add it to your home screen", "Open <b>Share</b> (it may be inside Safari's page menu). "
         "Scroll to <b>Add to Home Screen</b>. If missing, look under <b>Edit Actions</b>."),
        ("Keep the app option on", "Keep <b>Open as Web App</b> on if shown, then tap <b>Add</b>."),
        ("Open your new icon", "Tap <b>Knowledge Vine Portal</b> on your home screen. Sign in if asked, "
         "then choose your child to read shared updates."),
    ], "https://support.apple.com/guide/iphone/open-as-web-app-iphea86e5236/ios", "Apple")
    page(c, "Android / Chrome", 2, [
        ("Open the school portal", "Open the school's secure portal address in <b>Chrome</b>. "
         "If your email app opens its own browser, open the address in Chrome instead."),
        ("Open Chrome's menu", "Tap the <b>three-dot menu</b>. Choose <b>Add to Home screen</b>, "
         "then <b>Install app</b>. Some versions show <b>Install app</b> directly."),
        ("Confirm installation", "Follow the installation prompt. The portal may also offer an "
         "<b>Install Knowledge Vine Portal</b> button under <b>Add to your phone</b>."),
        ("Open your new icon", "Tap <b>Knowledge Vine Portal</b> on your home screen. Sign in if asked, "
         "then choose your child to read shared updates."),
    ], "https://developer.chrome.com/blog/how_chrome_helps_users_install_the_apps_they_value", "Chrome")
    c.save()
    print(OUT)


if __name__ == "__main__":
    main()
