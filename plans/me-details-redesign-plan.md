# Me → Tonight: "Change your details" redesign

15 Nov 2026. Plan only - nothing built yet.

## What is there now

The Your details card on Tonight is fine: a tidy list of what the desk has on
file and one **Ask the desk to change** button. The mess is the sheet that
button opens (`ydOpenSheet` → `ydFillSheet` in `relay/src/pages.ts`, around
line 16886), which is two whole forms, `detailsForm` and `crewSection`, poured
one after the other into Me's bottom sheet.

Measured on a 375 x 812 phone with a driver who has a PIN, is signed in and
has crew turned on: the sheet is **2,985px tall, about 4.4 screens**, with
three big buttons and three prose notes along the way.

What goes wrong, top to bottom:

1. **Two jobs in one sheet, each with its own Send.** "Send to the desk"
   sits in the middle, then "My crew" starts underneath with its own
   "Send my crew". Nothing tells you that typing a crew name and pressing
   the first Send sends nothing.
2. **"Forget my PIN on this phone" is stuck between the two forms.** It's
   a full-size tonal button that looks like the first control of My crew.
3. **Read-only rows look like fields.** "Name" and "Email" get the same
   bold label as the boxes you can type in, then a grey line underneath
   ("the desk changes this one", "On file: …"). You read them as broken
   inputs.
4. **Hints come after the box, in full sentences.** Every field has a
   label, then the box, then a two-line hint, so the labels and boxes don't
   line up and the gaps between fields change. The crew notes add a
   separate "19 of 80 characters." paragraph under each box.
5. **Checkboxes drawn as radio circles inside pill rows.** The privacy
   ticks look like radio buttons. One that's already on (locked) looks
   exactly like one you just picked, and its "On - ask at the desk to undo"
   is grey text inside it.
6. **Headings with no hierarchy.** "My crew" and "Your notes for the
   commentary" are `h4`s about the size of a field label, with odd gaps
   above and below. "Online privacy" is a boxed legend, and nothing else
   gets a box.
7. **Full-width "Play" buttons.** Each voice gets a 56px-tall tonal Play
   bar, the same weight as Forget PIN and Done.
8. **Legal and behaviour notes come after Send.** "Voice picks change
   straight away…" and "What you send may be said over the PA…" both
   appear below the button they explain. The consent line has to come
   *before* the tap.
9. **The guardian tick is in two places.** "I'm a parent or guardian
   making this change" shows on the details form, and for a junior
   "I'm making this change for them" shows again on crew.
10. **A tall tonal Done at the very end,** after 3,000px, while the
    sheet's grab handle and backdrop already close it.
11. **Signed out, it gets worse.** The PIN box sits at the top, and My crew
    underneath only says "Sign in with your personal PIN above".

The card's "Your crew" row ("Principal: Gravel Jones (Voice: Rachel) · Tech
lead: club's own") also runs on as one sentence.

## The redesign: one sheet per job, launched from the card

Keep the card. It's already the right "profile" view. Break the sheet into
**small, focused sheets** that each open from their own group on the card.
Each one fits on about a screen, with one Send in a sticky footer.

### The card (Tonight → Your details)

```
Your details                         🔒 Signed in · Sign out
┌─────────────────────────────────────────────┐
│ HOW YOU'RE CALLED                    Change › │
│ Name        Al Gorithim          desk only │
│ Announcer   Al Gor-ith-im                   │
│ Pronouns    he/him                          │
│ About you   Races a red buggy and loves…    │
├─────────────────────────────────────────────┤
│ EMAIL                                Change › │
│ al@example.test · Night email on            │
├─────────────────────────────────────────────┤
│ ONLINE PRIVACY                       Change › │
│ Not in videos                               │
├─────────────────────────────────────────────┤
│ MY CREW                              Change › │
│ Principal   Gravel Jones · Rachel           │
│ Tech lead   Club's own                      │
└─────────────────────────────────────────────┘
Nothing changes until the desk checks it.
```

- Four groups, each with a header row and a "Change ›" button (a 44px
  target, using the kit's `.more` / chevron style). Tapping it opens that
  group's sheet. The single "Ask the desk to change" button goes.
- The **PIN state moves to the card's header**: a small chip reading
  "Signed in · Sign out", or "Sign in to change" when signed out. That
  replaces the Forget-PIN bar in the sheet.
- A **request the desk hasn't answered yet shows on its own row** as a
  "Waiting for the desk" tag (from `sentAsks` / the live entry's asks), so
  the card stops looking unchanged after you send.
- The crew row splits into two lines, one per role.

### Sign-in comes first, once

If the driver has a PIN and this tab isn't signed in, any "Change ›"
opens a **sign-in sheet** first (the existing `pinGate`, alone in the
sheet). On success it goes straight on to the sheet they asked for. No
sheet ever shows a PIN box and a form together, and nothing says "sign in
above".

### Sheet 1: How you're called

```
How you're called                              ✕
Name        Al Gorithim             desk only 🔒
─────────────────────────────────────────────
Announcer name                           13/40
[ Al Gor-ith-im                              ]
Spell it the way it sounds.

Pronouns                                  6/24
[ he/him                                     ]

About you                                41/120
[ Races a red buggy and loves the hairpin    ]
One line the commentary can use.
─────────────────────────────────────────────
☐ I'm a parent or guardian making this change
[          Send to the desk          ]  ← sticky
```

- Read-only Name is a **locked row** (value plus a "desk only" tag), not
  a label.
- Each hint is **one short line**, and the character count sits **on the
  label line, right-aligned**, the same for every field. Long examples go
  in the placeholder ("e.g. she/her").
- The guardian tick sits right above Send, in this sheet and the others.

### Sheet 2: Email

- An "On file" locked row, then "New email address" (only when signed in
  with a PIN). Without a PIN, one line reads "Changing your email needs
  your personal PIN - ask at the desk." and there's no box.
- The "Email me my night after racing" switch sits in the same sheet, with
  the existing disable rule.

### Sheet 3: Online privacy

- One line at the top: "Changes the club's web pages only. The PA, big
  screen and Wi-Fi pages stay the same."
- Three **switch rows** (the kit's tonal row with a real toggle, not a
  radio circle). A switch that's already on shows a **lock icon and "On ·
  ask the desk to undo"** on its own line, and can't be tapped.
- It turns on only, as now. Send is disabled until something new is on.

### Sheet 4: My crew

```
My crew                                        ✕
Voices change straight away. Names and notes
wait for the desk.

┌ Team Principal ─────────────────────────────┐
│ Name                                  12/24 │
│ [ Gravel Jones                            ] │
│ Voice                                       │
│ [ Rachel                         ▾ ] [ ▶ ]  │
└─────────────────────────────────────────────┘
┌ Technical Lead ─────────────────────────────┐
│ …same…                                      │
└─────────────────────────────────────────────┘

Notes for the commentary
Tonight's goal                          19/80
[ Top 3 in the A main                      ]
Said before your race tonight only.
Thanks / shout-out                       0/80
Your car's story                        0/120

What you send may be said over the PA and on
the club's stream. By sending, you let your
club and HeatCaller use it in commentary.
☐ I'm making this change for them   (junior)
[            Send my crew            ]  ← sticky
```

- Each role is a **small card**. **Play becomes a 44px square icon
  button** next to the voice picker (with an aria-label, hidden when there's
  no sample), not a full-width bar.
- The behaviour line goes **at the top**, and the consent line goes
  **directly above Send**.
- A junior with desk-only notes gets the one-line "filled in at the desk"
  note in place of the three boxes.

### Shared pieces (the kit, not new looks)

- **Sticky sheet footer.** `meOpenSheet` grows an option to pin a given
  button, plus its status line, to the bottom of the sheet. Those sheets
  get no Done button; the ✕ and the backdrop close them. The other Me
  sheets (pack, late, at-the-track, sign-out) stay as they are.
- **Field row** with label, right-aligned counter, box and one-line hint,
  as one helper (`textField` gets a `count` option and puts the hint before
  the box) so every sheet has the same rhythm.
- **Locked row** (`dt`/`dd` with a lock and a "desk only" tag), shared by
  Name, the email on file and locked privacy switches.
- **Switch row** for privacy and night email: the kit's tonal pill with a
  real `role="switch"` checkbox.
- Kit tokens only, 44px+ targets, the focus ring, and no hex colours,
  following the Pit Wall rule. The Tonight layout in `mespaces.ts` stays
  as it is; only the card's insides and the sheets change.

## What stays the same

- Every request still goes through `sendAsk('details' | 'crew', f)` with
  the same fields and the same "send only what changed" logic. The relay
  needs **no server change**.
- The PIN pass stays in sessionStorage only, and a 403 still sends them
  back to sign-in with the relay's words.
- The fold fallback (a page without `#me-sheet`) keeps working. It can
  reuse the four group forms inside `<details>` folds.
- A redraw never wipes typing (`edited` / `refreshAsks`). That now applies
  per sheet.

## Build order

1. Sheet footer option in `meOpenSheet`, plus the field-row, locked-row
   and switch-row helpers and CSS.
2. Card regrouped into four groups with "Change ›", the PIN chip and the
   "waiting" tags.
3. The sign-in sheet, then onward to the target sheet.
4. Sheets 1-3, split out of `detailsForm` (one builder per group, sharing
   `base`).
5. Sheet 4, `crewForm` restyled: role cards, icon Play, notes moved.
6. Fold fallback rebuilt from the same builders.
7. Tests: update the relay page tests that look for "Ask the desk to
   change", "Forget my PIN on this phone" and the sheet's text, and add
   tests that each sheet sends only its own fields. Then screenshot all
   four sheets at 375px, signed in, signed out, junior and no PIN, in both
   themes, and check each sheet is about a screen tall.
8. Deploy the relay (`fly deploy` from `relay/`), since phones only get
   this after a deploy.

## Questions for you

- **Four sheets, or one sheet with tabs?** Separate sheets are the
  simplest and shortest. A segmented control (Called · Email · Privacy ·
  Crew) at the top of one sheet keeps a single entry point. I recommend
  separate sheets.
- **Keep "About you" for juniors?** The children-privacy audit flagged the
  about line as fix-now. This redesign is a natural place to make it
  desk-only for juniors, as crew names may become.
- **Sign out on the card:** a small chip (proposed) or a row at the bottom
  of the card?
