# Chinese text fallback

NotoSansCJKsc-Regular.otf is the unmodified Noto Sans CJK SC Regular font from
https://github.com/notofonts/noto-cjk/tree/main/Sans/OTF/SimplifiedChinese.
Its SIL Open Font License is included in LICENSE.txt.

The companion TMP font asset is registered in the global fallback list in
`Assets/TextMesh Pro/Resources/TMP Settings.asset`. Keep the existing primary
fonts (including Fredoka) on project text components. TMP uses this fallback
only for missing glyphs, including user-entered Chinese text.

The font uses Dynamic atlas population, 48-point SDF sampling, a 2048 x 2048
atlas and multiple atlas textures. Keep Include Font Data enabled on the source
font so new characters can be generated in builds. Dynamic data is cleared on
build; the bundled source font remains available. Chinese support must not be
registered or removed by the AI client or individual pages.

The TMP face's ascent, descent and line height are normalized to the existing
Fredoka SemiBold metrics at the same point size. Noto's original line box is
taller than the authored compact task rows, causing TMP's Ellipsis mode to hide
even short Chinese titles. Glyph size, outlines and the original OTF are unchanged.

Source OTF SHA-256:
`2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b`
