# Manual sources

HTML sources of the three PDF documents in `docs/manuals/`. Screenshots are in `img/`.

Rebuild a PDF (needs Node with Playwright/Chromium and Python with `pypdf`):

```bash
cd docs/manuals/source
node render.js user-manual.html ../VMM-UMS-AMS-User-Manual.pdf "UMS & AMS · User Manual"
node render.js technical-reference.html ../VMM-UMS-AMS-Technical-Logic-Reference.pdf "UMS & AMS · Technical Logic Reference"
node render.js business-rules.html ../VMM-UMS-AMS-Business-Rules-and-Validations.pdf "UMS & AMS · Business Rules & Validations"
```
