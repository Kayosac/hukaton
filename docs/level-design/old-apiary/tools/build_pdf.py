"""Render LEVEL_DESIGN.md and its checked figures. Does not touch Unity assets.

Usage: python3 tools/build_pdf.py [output.pdf]
Requires reportlab and Pillow; optional QA uses pypdf/pdfplumber/Poppler.
"""
from pathlib import Path
import re,sys,html,json,math,hashlib
from xml.sax.saxutils import escape
from reportlab.platypus import BaseDocTemplate,PageTemplate,Frame,Paragraph,Spacer,PageBreak,NextPageTemplate,Image,LongTable,TableStyle,Preformatted,KeepTogether,Flowable
from reportlab.platypus.tableofcontents import TableOfContents
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT,TA_CENTER
from reportlab.lib.pagesizes import A4,A3,landscape
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from PIL import Image as PILImage

HERE=Path(__file__).resolve().parents[1]
REPO=HERE.parents[2]
OUTPUT=Path(sys.argv[1]) if len(sys.argv)>1 else REPO/'output/pdf/OldApiary-Level-Design-RU.pdf'
OUTPUT.parent.mkdir(parents=True,exist_ok=True)
font_dirs=[Path('/Users/kayosac/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/libreoffice-headless/libreoffice/LibreOfficeDev.app/Contents/Resources/fonts/truetype'),Path('/usr/share/fonts/truetype/dejavu'),Path('/System/Library/Fonts/Supplemental')]
def font_path(names):
 for folder in font_dirs:
  for name in names:
   p=folder/name
   if p.exists():return str(p)
 raise RuntimeError('A Cyrillic font is required: DejaVu Sans or Arial')
pdfmetrics.registerFont(TTFont('Body',font_path(['DejaVuSans.ttf','Arial.ttf'])))
pdfmetrics.registerFont(TTFont('BodyBold',font_path(['DejaVuSans-Bold.ttf','Arial Bold.ttf'])))
pdfmetrics.registerFont(TTFont('BodyItalic',font_path(['DejaVuSans-Oblique.ttf','Arial Italic.ttf'])))
pdfmetrics.registerFontFamily('Body',normal='Body',bold='BodyBold',italic='BodyItalic',boldItalic='BodyBold')
INK=colors.HexColor('#24322f');MUTED=colors.HexColor('#5a6b66');GREEN=colors.HexColor('#48634e');PALE=colors.HexColor('#edf1e9');RULE=colors.HexColor('#c7d0c8');ORANGE=colors.HexColor('#a4562f')
styles={
 'body':ParagraphStyle('body',fontName='Body',fontSize=10.2,leading=14.8,textColor=INK,spaceAfter=6,splitLongWords=True,allowWidows=0,allowOrphans=0),
 'h1':ParagraphStyle('h1',fontName='BodyBold',fontSize=20,leading=25,textColor=GREEN,spaceBefore=4,spaceAfter=18,keepWithNext=True),
 'h2':ParagraphStyle('h2',fontName='BodyBold',fontSize=13,leading=18,textColor=INK,spaceBefore=14,spaceAfter=8,keepWithNext=True),
 'h3':ParagraphStyle('h3',fontName='BodyBold',fontSize=11,leading=16,textColor=INK,spaceBefore=12,spaceAfter=6,keepWithNext=True),
 'bullet':ParagraphStyle('bullet',fontName='Body',fontSize=10.2,leading=15.1,textColor=INK,leftIndent=13,bulletIndent=1,spaceAfter=5),
 'table':ParagraphStyle('table',fontName='Body',fontSize=8.8,leading=12.3,textColor=INK,spaceAfter=0),
 'tablehead':ParagraphStyle('tablehead',fontName='BodyBold',fontSize=8.8,leading=12.3,textColor=GREEN,spaceAfter=0),
 'caption':ParagraphStyle('caption',fontName='Body',fontSize=9,leading=13,textColor=MUTED,spaceBefore=8,spaceAfter=10),
 'code':ParagraphStyle('code',fontName='Body',fontSize=8.5,leading=11.5,textColor=INK,leftIndent=10,backColor=PALE,spaceBefore=8,spaceAfter=10),
 'toc1':ParagraphStyle('toc1',fontName='BodyBold',fontSize=10.5,leading=15,textColor=GREEN,spaceBefore=7,leftIndent=0,firstLineIndent=0),
 'toc2':ParagraphStyle('toc2',fontName='Body',fontSize=9,leading=12.5,textColor=INK,leftIndent=12,firstLineIndent=0,spaceBefore=2),
}

def normalize(s):return s.replace('\u2011','-').replace('\u2013','-').replace('\u2014','-').replace('\u00ad','').replace('\u202f',' ')
def inline(s):
 s=normalize(s);s=escape(s)
 s=re.sub(r'`([^`]+)`',lambda m:'<font size="9.1">'+m.group(1)+'</font>',s)
 s=re.sub(r'\*\*([^*]+)\*\*',r'<b>\1</b>',s)
 s=re.sub(r'\[([^\]]+)\]\(([^)]+)\)',lambda m:('<link href="'+m.group(2)+'" color="#48634e">'+m.group(1)+'</link>') if re.match(r'^(https?://|kayospace:)',m.group(2)) else m.group(1),s)
 s=re.sub(r'\[(S\d{2}(?:, S\d{2})*)\]',lambda m:'['+', '.join('<link href="#source-'+z+'" color="#48634e">'+z+'</link>'for z in m.group(1).split(', '))+']',s)
 src=re.match(r'<b>(S\d{2})\.',s)
 if src:s='<a name="source-'+src.group(1)+'"/>'+s
 return s

class Cover(Flowable):
 def __init__(self):super().__init__();self.width=490;self.height=690
 def draw(self):
  c=self.canv
  c.setFillColor(GREEN);c.setFont('BodyBold',11);c.drawString(0,664,'HAKATON / ТИХАЯ ЛОЩИНА')
  c.setFillColor(INK);c.setFont('BodyBold',31);c.drawString(0,608,'Старая пасека')
  c.setFont('Body',17);c.drawString(0,577,'Подробный дизайн игрового уровня')
  c.setFillColor(MUTED);c.setFont('Body',10.5);c.drawString(0,546,'Версия 1.1  /  9 ноября 2026  /  Проект для блокаута')
  c.setStrokeColor(RULE);c.line(0,521,490,521)
  metrics=[('150 × 120 м','габарит'),('22','точки интереса'),('18','преград'),('5','мостов')]
  for i,(value,name)in enumerate(metrics):
   x=[0,195,300,405][i];c.setFillColor(GREEN);c.setFont('BodyBold',19);c.drawString(x,486,value);c.setFillColor(MUTED);c.setFont('Body',9.5);c.drawString(x,466,name)
  im=HERE/'figures/map-overview.png'
  if im.exists():c.drawImage(str(im),40,107,width=410,height=344,preserveAspectRatio=True,mask='auto')
  p=Paragraph('Первый полноценный уровень с плотными пешими петлями, осмысленными водными преградами и запасом для развития колоды.',styles['body']);p.wrap(490,70);p.drawOn(c,0,59)
  c.setFillColor(MUTED);c.setFont('Body',9);c.drawString(0,18,'География · карты · монстры · ассеты · экономика · Unity · плейтесты')

class Book(BaseDocTemplate):
 def __init__(self,path):
  super().__init__(str(path),pagesize=A4,leftMargin=52,rightMargin=52,topMargin=53,bottomMargin=48,title='Старая пасека — подробный дизайн уровня',author='Hakaton',subject='Первый полноценный уровень 150 × 120 м, версия 1.1',pageCompression=1)
  self.chapter='Старая пасека';self.heading_index=0
  self.addPageTemplates([PageTemplate(id='Text',pagesize=A4,frames=[Frame(52,48,A4[0]-104,A4[1]-101,id='normal',leftPadding=0,rightPadding=0,topPadding=0,bottomPadding=0)],onPage=self.page),PageTemplate(id='MapWide',pagesize=landscape(A3),frames=[Frame(42,45,landscape(A3)[0]-84,landscape(A3)[1]-90,id='wide',leftPadding=0,rightPadding=0,topPadding=0,bottomPadding=0)],onPage=self.page),PageTemplate(id='MapTall',pagesize=A3,frames=[Frame(42,45,A3[0]-84,A3[1]-90,id='tall',leftPadding=0,rightPadding=0,topPadding=0,bottomPadding=0)],onPage=self.page)])
 def beforeDocument(self):self.heading_index=0;self.chapter='Старая пасека';self.outline_level=-1
 def page(self,canvas,doc):
  size=doc.pageTemplate.pagesize;canvas.setPageSize(size)
  if doc.page==1:return
  canvas.saveState();canvas.setStrokeColor(RULE);canvas.setLineWidth(.5);canvas.line(42,size[1]-31,size[0]-42,size[1]-31)
  canvas.setFillColor(MUTED);canvas.setFont('Body',8);canvas.drawString(42,size[1]-23,'HAKATON  /  СТАРАЯ ПАСЕКА  /  ДИЗАЙН УРОВНЯ')
  canvas.drawString(42,24,'Версия 1.1 · 09.11.2026');canvas.drawRightString(size[0]-42,24,str(doc.page));canvas.restoreState()
 def afterFlowable(self,f):
  if isinstance(f,Paragraph)and getattr(f,'toc_level',None)is not None:
   self.heading_index+=1;key='h'+str(self.heading_index);text=f.getPlainText();self.canv.bookmarkPage(key)
   outline_level=min(f.toc_level,self.outline_level+1);self.canv.addOutlineEntry(text,key,level=outline_level,closed=outline_level==0);self.outline_level=outline_level
   self.notify('TOCEntry',(f.toc_level,text,self.page,key))

book=Book(OUTPUT)
story=[Cover(),PageBreak()]
lines=(HERE/'LEVEL_DESIGN.md').read_text().splitlines();i=1;manual_toc=False
toc=TableOfContents();toc.levelStyles=[styles['toc1'],styles['toc2']];toc.dotsMinLevel=0
headings=[];table_count=0;image_count=0;chapter_started=False

def heading(text,level):
 p=Paragraph(inline(text),styles['h'+str(min(level,3))]);p.toc_level=0 if level==1 else 1 if level==2 else None
 return p
def table(rows):
 global table_count
 table_count+=1;n=len(rows[0]);available=A4[0]-104
 # Use conservative widths for short IDs and more space for operational descriptions.
 if rows[0][0]in ('ID','Находка'):weights=([.075,.19,.345,.39] if n==4 else [.10,.08,.29,.38,.15])
 elif n==2:weights=[.27,.73]
 elif n==3:weights=[.36,.32,.32]
 elif n==4:weights=[.17,.29,.28,.26]
 elif n==5:weights=[.10,.18,.22,.28,.22]
 else:weights=[1/n]*n
 widths=[available*x/sum(weights)for x in weights]
 data=[[Paragraph(inline(cell),styles['tablehead' if ri==0 else 'table'])for cell in row]for ri,row in enumerate(rows)]
 t=LongTable(data,colWidths=widths,repeatRows=1,hAlign='LEFT')
 t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),PALE),('LINEBELOW',(0,0),(-1,0),.7,GREEN),('LINEBELOW',(0,1),(-1,-1),.25,RULE),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),6),('RIGHTPADDING',(0,0),(-1,-1),6),('TOPPADDING',(0,0),(-1,-1),6),('BOTTOMPADDING',(0,0),(-1,-1),6)]))
 return t
def imageflow(alt,rel):
 global image_count
 image_count+=1;p=HERE/rel
 if not p.exists():raise FileNotFoundError(p)
 w,h=PILImage.open(p).size
 if p.name.startswith('map-'):
  plate_title=alt
  plate_intro=[]
  if len(story)>=2 and isinstance(story[-2],Paragraph) and getattr(story[-2],'toc_level',None)==0 and isinstance(story[-1],Paragraph) and getattr(story[-1],'toc_level',None) is None:
   intro=story.pop();chapter_heading=story.pop();plate_intro=[chapter_heading,intro]
   if story and isinstance(story[-1],PageBreak):story.pop()
  if story and isinstance(story[-1],Paragraph) and getattr(story[-1],'toc_level',None)==1:
   plate_title=story.pop().getPlainText()+' · '+alt
  template='MapTall' if h/w>1.05 else 'MapWide';size=A3 if template=='MapTall' else landscape(A3);maxw=size[0]-90;maxh=size[1]-170-(115 if plate_intro else 0)
  factor=min(maxw/w,maxh/h)
  if len(story)>=2 and isinstance(story[-2],NextPageTemplate) and isinstance(story[-1],PageBreak):
   story.pop();story.pop()
  story.extend([NextPageTemplate(template),PageBreak()]+plate_intro+[heading(plate_title,2),Image(str(p),width=w*factor,height=h*factor)])
  story.append(Paragraph('Север сверху. 01–22 — точки интереса; П01–П18 — преграды; М1–М5 — постоянные мосты. Серые линии — пешие связи, пунктир — пути с картами, треугольники — существа, ромбы — находки. Геометрия схемы условна; точные рабочие размеры приведены в паспортах.',styles['caption']))
  story.extend([NextPageTemplate('Text'),PageBreak()])
 else:
  maxw=A4[0]-104;factor=min(maxw/w,300/h)
  preceding=[]
  if story and isinstance(story[-1],Paragraph) and getattr(story[-1],'toc_level',None) is not None:preceding=[story.pop()]
  story.append(KeepTogether(preceding+[Image(str(p),width=w*factor,height=h*factor),Paragraph(inline(alt),styles['caption'])]))

while i<len(lines):
 l=lines[i].strip()
 if not l:i+=1;continue
 if l=='## Содержание':
  story.extend([PageBreak(),heading('Содержание',1),toc,PageBreak()]);manual_toc=True;i+=1;continue
 if manual_toc:
  if l.startswith('# 1 '):manual_toc=False
  else:i+=1;continue
 if l.startswith('```'):
  block=[];i+=1
  while i<len(lines)and not lines[i].strip().startswith('```'):block.append(normalize(lines[i]));i+=1
  story.append(Preformatted('\n'.join(block),styles['code']));i+=1;continue
 m=re.match(r'^(#{1,3}) (.+)',l)
 if m:
  level=len(m[1]);txt=m[2]
  if level==1:
   if chapter_started:story.append(PageBreak())
   chapter_started=True
  story.append(heading(txt,level));headings.append((level,txt));i+=1;continue
 m=re.match(r'^!\[([^\]]*)\]\(([^)]+)\)$',l)
 if m:imageflow(m[1],m[2]);i+=1;continue
 if l.startswith('|'):
  rows=[]
  while i<len(lines)and lines[i].strip().startswith('|'):
   row=[x.strip()for x in lines[i].strip().strip('|').split('|')]
   if not all(re.fullmatch(r':?-{2,}:?',c)for c in row):rows.append(row)
   i+=1
  story.append(table(rows));story.append(Spacer(1,10));continue
 m=re.match(r'^(- |\d+\. )(.*)',l)
 if m:
  bullet='•' if m[1]=='- ' else m[1].strip();story.append(Paragraph(inline(m[2]),styles['bullet'],bulletText=bullet));i+=1;continue
 paragraph=[l];i+=1
 while i<len(lines)and lines[i].strip()and not re.match(r'^(#|\||!\[|```|- |\d+\. )',lines[i].strip()):paragraph.append(lines[i].strip());i+=1
 story.append(Paragraph(inline(' '.join(paragraph)),styles['body']))

cleaned=[]
for item in story:
 if isinstance(item,PageBreak) and cleaned and isinstance(cleaned[-1],PageBreak):continue
 cleaned.append(item)
book.multiBuild(cleaned)
manifest={'pdf':str(OUTPUT),'source':str(HERE/'LEVEL_DESIGN.md'),'headings':len(headings),'tables':table_count,'figures':image_count,'source_sha256':hashlib.sha256((HERE/'LEVEL_DESIGN.md').read_bytes()).hexdigest(),'pdf_sha256':hashlib.sha256(OUTPUT.read_bytes()).hexdigest()}
(HERE/'data/build-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
print(json.dumps(manifest,ensure_ascii=False,indent=2))
