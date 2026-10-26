// Renders a docs/*.html file to PDF with Chromium (A4, CSS page rules). The cover is rendered without the footer;
// the body pages get a footer with the title and page numbers. The two parts are merged with pypdf.
const { chromium } = require('playwright');
const { execFileSync } = require('child_process');
const path = require('path');
const [, , input, output, title] = process.argv;
(async () => {
  const browser = await chromium.launch();
  const render = async (css, file, footer) => {
    const page = await browser.newPage();
    await page.goto('file://' + path.resolve(input), { waitUntil: 'networkidle' });
    await page.addStyleTag({ content: css });
    await page.evaluate(() => document.fonts.ready);
    await page.pdf({
      path: file, format: 'A4', printBackground: true, preferCSSPageSize: true, displayHeaderFooter: footer,
      headerTemplate: '<span></span>',
      footerTemplate: `<div style="width:100%;font-family:Inter,sans-serif;font-size:7.5pt;color:#6b7a90;padding:0 16mm;display:flex;justify-content:space-between">
        <span>${title}</span><span>Vishal Mega Mart · Internal</span><span>Page <span class="pageNumber"></span> of <span class="totalPages"></span></span></div>`,
    });
    await page.close();
  };
  const cover = output + '.cover.pdf', body = output + '.body.pdf';
  await render('body > :not(.cover) { display: none !important; } .cover { page-break-after: auto; }', cover, false);
  await render('.cover { display: none !important; } @page :first { margin: 20mm 16mm 18mm 16mm; }', body, true);
  await browser.close();
  execFileSync('python3', ['-I', '-c', `
import sys
from pypdf import PdfReader, PdfWriter
w = PdfWriter()
for f in sys.argv[1:3]:
    for p in PdfReader(f).pages: w.add_page(p)
w.add_metadata({'/Title': sys.argv[4], '/Author': 'Vishal Mega Mart · IT Applications'})
with open(sys.argv[3], 'wb') as o: w.write(o)
`, cover, body, output, title]);
  require('fs').unlinkSync(cover); require('fs').unlinkSync(body);
  console.log('wrote', output);
})();
