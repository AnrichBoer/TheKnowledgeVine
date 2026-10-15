# Portal and website logo

Updated 14 October 2026 at the user's request. The supplied square Knowledge Vine Montessori logo is the reference for a compact horizontal header mark. It preserves the K/vine crest, open book, serif school name and green/gold palette. The small tagline is omitted from the header version for readability.

- Master PNG: `../knowledevinefront/design/brand/knowledge-vine-horizontal.png` (2079 × 756, transparent).
- Web asset: `../knowledevinefront/src/assets/images/knowledge-vine-brand.webp` (1039 × 378, transparent; about 90 KB).
- Used in the public website header, portal login, password setup/help/reset screens and signed-in portal header.
- Cream and sage backgrounds show through the asset; no white background card or stretching. On phones the login logo is centred in a compact banner above the form.
- Existing email templates keep their text wordmark. This styling change does not send emails.

Generated with the built-in image-generation tool using `/home/anrich/Downloads/Picture1.png` as its reference, with `transparent_background: true`. The PNG was copied into the project and resized/encoded as WebP without removing its alpha channel. Original legacy logo assets are retained.

## Verification and preview

The production Angular build passes its bundle budgets and all 38 frontend tests pass. The frontend was deployed to the testing VM by rebuilding and recreating only the KnowledgeVine web service. Browser checks confirmed the new asset loads on login, password help and the public header, with no horizontal overflow at desktop or 390px phone width; the phone navigation still opens and leads to the portal. The signed-in header uses the same asset through its compiled template.

- [VM login](http://127.0.0.1:8803/portal/login)
- [Desktop screenshot](portal-designs/logo-update-desktop.jpg)
- [Phone screenshot](portal-designs/logo-update-phone.jpg)

## Final generation prompt

Create a polished horizontal website-header adaptation of the attached Knowledge Vine Montessori logo on an ACTUALLY TRANSPARENT background. Preserve the supplied logo's distinctive forest-green capital K wrapped in a delicate circular vine with sage leaves and a small grape cluster, with the gold open book at its base. Keep that crest on the LEFT; place the elegant serif school wordmark on the RIGHT. Exact text, spelled precisely: small 'THE' centered above larger 'KNOWLEDGE VINE', with smaller widely spaced gold 'MONTESSORI' below. Do not include the tiny tagline from the reference in this compact header adaptation. Preserve the refined botanical school identity, thin elegant lines, and flat forest green (#184f36), sage green, and muted gold (#aa8b46) colours. The overall mark should be a wide, compact, balanced 3.5:1 horizontal composition, filling its transparent canvas with only small clear margins. Clean crisp edges and readable lettering. No white rectangle or other background, no mockup, no shadows, no gradients, no new decorative elements. This will be displayed at approximately 340px wide and 100px high on pale cream or sage website backgrounds.
