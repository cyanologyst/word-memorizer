from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

import pdfplumber


HEADING_RE = re.compile(r"^(\d{1,2})\.\s*(.+?)\s*\(\s*([^)]+?)\s*\)\s*(.*)$")
HEADER_PREFIXES = (
    "550 Must-know Words",
    "www.ibtil.org",
    "Chapter",
    "One",
    "Two",
    "Three",
    "Four",
    "Five",
    "Six",
    "Seven",
    "Eight",
    "Nine",
    "Ten",
)


def clean_pronunciation(raw: str) -> str:
    value = raw.replace("→", "").strip()
    if value and not value.startswith("/"):
        value = "/" + value
    if value and not value.endswith("/"):
        value = value + "/"
    return value


def clean_meaning_line(line: str) -> str:
    value = line.replace("\uf0b7", "").replace("•", "").strip()
    value = re.sub(r"\s+", " ", value)
    return value.strip(" -")


def is_noise(line: str) -> bool:
    stripped = line.strip()
    return (
        not stripped
        or stripped.isdigit()
        or any(stripped.startswith(prefix) for prefix in HEADER_PREFIXES)
        or stripped in {"(noun)", "(verb)", "(adjective)", "(adverb)"}
    )


def extract_entries(pdf_path: Path) -> list[dict]:
    entries: list[dict] = []
    current: dict | None = None
    previous_number: int | None = None
    chapter = 1

    with pdfplumber.open(pdf_path) as pdf:
        for page_number, page in enumerate(pdf.pages, start=1):
            text = page.extract_text(x_tolerance=1, y_tolerance=3) or ""
            for line in text.splitlines():
                heading = HEADING_RE.match(line)
                if heading:
                    number = int(heading.group(1))
                    if previous_number is not None and number < previous_number:
                        chapter += 1

                    current = {
                        "page": page_number,
                        "chapter": chapter,
                        "chapter_order": number,
                        "term": heading.group(2).strip(),
                        "part_of_speech": heading.group(3).strip(),
                        "pronunciation": clean_pronunciation(heading.group(4)),
                        "meaning_lines": [],
                    }
                    entries.append(current)
                    previous_number = number
                    continue

                if current is None or is_noise(line):
                    continue

                if line.startswith("Meaning:"):
                    current["meaning_lines"].append(clean_meaning_line(line.removeprefix("Meaning:")))
                elif not current["meaning_lines"] and not line.startswith(("\uf0b7", "•")):
                    current["meaning_lines"].append(clean_meaning_line(line))
                elif current["meaning_lines"] and len(current["meaning_lines"]) < 2:
                    value = clean_meaning_line(line)
                    if value and not value.endswith("."):
                        current["meaning_lines"].append(value)

    return entries


def build_wordlist(entries: list[dict]) -> dict:
    words = []
    for index, entry in enumerate(entries, start=1):
        meaning = " ".join(entry["meaning_lines"]).strip()
        words.append(
            {
                "id": f"toefl-550-march-2026-{index:03d}",
                "term": entry["term"],
                "partOfSpeech": entry["part_of_speech"],
                "pronunciation": entry["pronunciation"],
                "shortMeaning": meaning,
                "chapter": entry["chapter"],
                "order": index,
                "tags": ["TOEFL"],
            }
        )

    return {
        "schemaVersion": 1,
        "id": "toefl-550-march-2026",
        "title": "550 Must-know Words for TOEFL iBT",
        "language": "en",
        "source": "User-provided PDF, updated March 2026",
        "words": words,
    }


def validate(wordlist: dict) -> None:
    words = wordlist["words"]
    if len(words) != 550:
        raise SystemExit(f"Expected 550 entries, extracted {len(words)}")

    ids = {word["id"] for word in words}
    if len(ids) != len(words):
        raise SystemExit("Word ids are not unique")

    empty_terms = [word["id"] for word in words if not word["term"].strip()]
    if empty_terms:
        raise SystemExit(f"Empty terms found: {empty_terms}")

    chapter_counts: dict[int, int] = {}
    for word in words:
        chapter_counts[word["chapter"]] = chapter_counts.get(word["chapter"], 0) + 1

    expected = {chapter: 60 for chapter in range(1, 10)}
    expected[10] = 10
    if chapter_counts != expected:
        raise SystemExit(f"Unexpected chapter counts: {chapter_counts}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Extract TOEFL vocabulary words into app wordlist JSON.")
    parser.add_argument("pdf", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    entries = extract_entries(args.pdf)
    wordlist = build_wordlist(entries)
    validate(wordlist)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(wordlist, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Wrote {len(wordlist['words'])} words to {args.output}")


if __name__ == "__main__":
    main()
