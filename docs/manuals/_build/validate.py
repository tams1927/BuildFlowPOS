from docx import Document
from docx.oxml.ns import qn
import os
for f in ['SuperAdminManual.docx','TenantAdminManual.docx','CashierQuickGuide.docx']:
    p=os.path.join('docs','manuals',f)
    d=Document(p)
    imgs=len(d.inline_shapes)
    paras=len(d.paragraphs)
    h1=sum(1 for x in d.paragraphs if x.style.name=='Heading 1')
    # crude page estimate: count explicit page breaks + heuristics
    breaks=0
    for para in d.paragraphs:
        for r in para.runs:
            breaks += r._r.xml.count('w:type=\"page\"')
    rels=sum(1 for r in d.part.rels.values() if 'image' in r.reltype)
    print(f'{f}: paragraphs={paras} headings1={h1} inline_images={imgs} image_rels={rels} pagebreaks={breaks}')
