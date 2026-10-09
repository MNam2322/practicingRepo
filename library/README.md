# The library (knowledge base)

108 chapters, about 950,000 words, on programming, .NET, databases, APIs, frontend, security,
shipping and architecture. This app is built **using** it: before designing a feature, look up what
the library says and follow it.

**Never read whole chapters or many files.** It is about 7 MB. Instead:

```
node library/find.mjs "overlapping time ranges"        # search by topic: prints chapter, heading, line
node library/show.mjs 1-2 1234                          # read just that section (line from find.mjs)
node library/show.mjs 7-4 "optimistic"                  # or by heading words
node library/show.mjs 7-4                               # list a chapter's sections
```

- `index.json`: every chapter with id, title, words and a one-line summary.
- `outline.json`: every chapter's `###` headings.
- `quick-look.md`: a few lines per chapter, for orientation.
- `chapters/`: the chapters themselves. Read-only.

Chapter structure: core explanation first, then `### In practice: realistic examples`, then "Going deeper"
groups (a `###` heading followed by `<p class="src">From ...</p>`). Chapters 4.9 (design patterns) and
7.9 (database optimisation) are casebooks of real before/after cases.
