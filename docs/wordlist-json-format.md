# Wordlist JSON format

Word Review Reminder imports vocabulary databases from files ending in `.wordlist.json`.

## Required structure

```json
{
  "schemaVersion": 1,
  "id": "my-wordlist-id",
  "title": "My Wordlist",
  "language": "en",
  "source": "Optional source note",
  "words": [
    {
      "id": "my-wordlist-id-001",
      "term": "Dissolve",
      "partOfSpeech": "verb",
      "pronunciation": "/dɪˈzɑːlv/",
      "shortMeaning": "melt, thaw, soften",
      "chapter": 1,
      "order": 1,
      "tags": ["TOEFL"]
    }
  ]
}
```

## Required fields

- `schemaVersion`: Must be `1`.
- `id`: A stable unique id for the whole list. Use lowercase letters, numbers, and hyphens.
- `title`: The name shown in the app.
- `words`: Array of vocabulary entries.
- `words[].id`: A stable unique id for this word.
- `words[].term`: The word or phrase to review.

## Optional fields

- `language`: Language code such as `en`.
- `source`: A short note about where the list came from.
- `words[].partOfSpeech`: Example: `noun`, `verb`, `adjective`.
- `words[].pronunciation`: IPA or another pronunciation note.
- `words[].shortMeaning`: A compact meaning shown on reminder cards.
- `words[].chapter`: Group number from the source material.
- `words[].order`: Display/import order.
- `words[].tags`: Labels such as `TOEFL`, `biology`, or `week-1`.

## Adding a list in the app

1. Save your database as a UTF-8 JSON file with the suffix `.wordlist.json`.
2. Open Word Review Reminder.
3. Go to `Wordlists`.
4. Select `Import JSON`.
5. Fix any validation errors shown by the app, then import again.

The importer checks required fields, duplicate ids, empty terms, and duplicate terms after normalization.
