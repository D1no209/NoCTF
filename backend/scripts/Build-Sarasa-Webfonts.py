"""Split official Sarasa SC TTFs for unicode-range delivery without dropping characters.

Requires fonttools[woff] 4.60.1. See ClientApp/app/assets/fonts/README.md.
"""
import argparse
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path
import hashlib
import json

from fontTools import subset
from fontTools.ttLib import TTFont


def ranges(points):
    result = []
    start = end = None
    for point in sorted(points):
        if start is None:
            start = end = point
        elif point == end + 1:
            end = point
        else:
            result.append(f'U+{start:X}' if start == end else f'U+{start:X}-{end:X}')
            start = end = point
    if start is not None:
        result.append(f'U+{start:X}' if start == end else f'U+{start:X}-{end:X}')
    return ','.join(result)


def variation_pairs(font):
    return {(selector, point) for table in font['cmap'].tables if table.format == 14
            for selector, mappings in table.uvsDict.items() for point, _ in mappings}


def build_face(arguments):
    source, destination, family, weight = arguments
    source = Path(source)
    destination = Path(destination)
    with TTFont(source, recalcTimestamp=False) as original:
        coverage = set(original.getBestCmap())
        variations = variation_pairs(original)
    selectors = {selector for selector, _ in variations}
    basic = {point for point in coverage if point <= 0x024F or 0x2000 <= point <= 0x206F
             or 0x3000 <= point <= 0x303F or 0xFF00 <= point <= 0xFFEF}
    common = set()
    # GB2312 level-one Han characters; uncommon names and extensions remain available below.
    for lead in range(0xB0, 0xD8):
        for trail in range(0xA1, 0xFF):
            try:
                common.add(ord(bytes((lead, trail)).decode('gb2312')))
            except UnicodeDecodeError:
                pass
    common = (common & coverage) - basic
    extra = sorted(coverage - basic - common)
    parts = [('basic', basic), ('common', common)]
    parts.extend((f'extra-{index // 2048:02}', set(extra[index:index + 2048]))
                 for index in range(0, len(extra), 2048))
    collected = set()
    collected_variations = set()
    records = []
    css = []
    for label, points in parts:
        if not points:
            continue
        assert not collected.intersection(points)
        options = subset.Options()
        options.layout_features = ['*']
        options.notdef_glyph = True
        options.notdef_outline = True
        options.recalc_timestamp = False
        font = TTFont(source, recalcTimestamp=False)
        worker = subset.Subsetter(options=options)
        worker.populate(unicodes=points | selectors)
        worker.subset(font)
        font.flavor = 'woff2'
        filename = f'{source.stem}-{label}.woff2'
        target = destination / filename
        font.save(target)
        font.close()
        if label in ('basic', 'common'):
            assert target.stat().st_size <= (100_000 if label == 'basic' else 1_000_000)
        with TTFont(target) as check:
            actual = set(check.getBestCmap())
            # Bidi mirroring may retain additional source codepoints in a subset.
            # CSS still advertises disjoint ranges, and no requested character may disappear.
            assert points <= actual <= coverage, (filename, len(points), len(actual))
            collected_variations.update(variation_pairs(check))
            assert check['OS/2'].usWeightClass == weight
        collected.update(points)
        records.append({'file': filename, 'bytes': target.stat().st_size,
                        'characters': len(points), 'unicodeRange': ranges(points),
                        'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
        css.append(f"@font-face {{\n  font-family: '{family}';\n  src: url('./{filename}') format('woff2');\n"
                   f'  font-weight: {weight};\n  font-style: normal;\n  font-display: swap;\n'
                   f'  unicode-range: {ranges(points)};\n}}\n')
    assert collected == coverage
    assert collected_variations == variations, (source.name, len(variations), len(collected_variations))
    return {'source': source.name, 'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
            'characters': len(coverage), 'variationSequences': len(variations),
            'parts': records}, ''.join(css)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    work = [(str(args.source / f'{stem}-{face}.ttf'), str(args.output), family, weight)
            for stem, family in [('SarasaUiSC', 'Sarasa UI SC'), ('SarasaMonoSC', 'Sarasa Mono SC')]
            for face, weight in [('Regular', 400), ('Bold', 700)]]
    with ProcessPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(build_face, work))
    (args.output / 'fonts.css').write_text('/* Generated by backend/scripts/Build-Sarasa-Webfonts.py. */\n'
                                        + ''.join(css for _, css in results), encoding='utf-8')
    manifest = {'version': '1.0.42', 'faces': [record for record, _ in results]}
    (args.output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'faces': len(results), 'files': sum(len(record['parts']) for record, _ in results),
                      'completeCharacterCoverage': True, 'completeVariationSequences': True}))


if __name__ == '__main__':
    main()
