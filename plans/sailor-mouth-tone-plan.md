# Sailor-mouth commentary tone: plan (15 Nov 2026)

A new choice in the Commentary page's Tone dropdown that swears and is crude.
It is never racist or antisemitic, and it never uses slurs of any kind.

## Where it slots in

The tones live in one table, `CommentaryTones.All` (CommentaryTones.cs). Each
character tone is a single `ToneProfile` row. The dropdown (CommentaryControl.cs:746)
and all five prompt builders in CommentaryService.cs pick the row up from there, so
the new tone is mostly one more row. The relay brain (Claude Haiku) only gets the
prompt the app builds, so the relay itself should not need to change. Check
brain.ts for any content screening before you rule out a deploy.

## Steps

1. **Enum value.** Add `Crude = 12` to `CommentaryTone` (CommentaryService.cs:10).
   Numbers 3, 4, 5, 6, 9 and 11 are retired and must never be reused.

2. **A per-tone rule.** At the moment every character tone gets the same
   `CharacterRule` appended, and that rule says "family-friendly". Change it like this:
   - Add a `Rule` field to `ToneProfile`, defaulting to `CharacterRule`.
   - Point both call sites at `character.Rule`: CommentaryService.cs:1092 for the
     voice and :5376 for the pit-lane host.
   - Give Crude its own `CrudeRule`. It keeps "facts come first" word for word, then sets these hard lines:
     - Swearing and crude humour are allowed. Aim them at the race, the luck, the
       track and the gap, never at a driver as a person.
     - Never racist, antisemitic or hateful about race, religion, ethnicity,
       nationality, sex, sexuality, gender identity or disability. No slurs, not
       even "as a joke" and not even softened.
     - Nothing sexual about anyone. Junior drivers race at these clubs.
     - No gore, real injury or death.

3. **The tone row.** Label it `"Adult: Crude - swears like a pit crew, 18+"`. The tone
   line carries a worked example on the usual fact, as every other row does, e.g.
   `"Four tenths, Sam. Four bloody tenths, and Bill's fucking stuck there."`
   Each tone also needs its own segment, exchange, close and host lines.
   ElevenLabs cues: `[laughs]` / `[annoyed]`.

4. **A safety net after generation.** Add a small word list of slurs and hate terms
   (slurs only, not swear words), checked on every generated line before it is spoken
   or shown. The check runs in every tone, not just Crude. A hit drops the line
   silently, following the "drop rather than say it badly" rule from stale calls.
   It also counts toward the Health page. This net is what makes the "never" a promise
   rather than a hope, since the prompt alone can't guarantee it.

5. **Opt-in.** Choosing Crude opens a one-time confirm: "This tone swears. Use it only
   where everyone in the room is fine with that." If they cancel, the dropdown goes
   back to the previous tone. The Support bundle already logs the tone.

6. **What gets written down vs. said.** The voice says the line uncensored. Anything
   that ends up shared or public stays clean:
   - Text on the phone pages, the OBS overlay and the race video captions is
     starred (`f***`). This uses a small mask list, separate from the slur list.
   - The night recap and honours writing use Enthusiastic when the tone is Crude,
     because people send those around.

7. **Tests** (CharacterToneTests.cs):
   - Update the count from 3 to 4 and add the enum value check for `Crude = 12`.
   - The Crude system prompt contains `CrudeRule` and does not contain "family-friendly".
     The other tones still contain `CharacterRule`.
   - The slur filter drops a line that contains a listed term, in any tone.
   - The screen mask stars swears, and the spoken text is left alone.
   - The recap and honours prompts never carry the Crude tone line.

8. **Try it for real.** Run some races with the tone on through the relay brain and read
   what comes back. Look at how crude it actually gets, whether Haiku holds back, and
   whether the facts stay exact.

## Decided (15 Nov 2026)

- **Name: Sailor-mouth.** Use `CommentaryTone.SailorMouth = 12`, with the label
  `"Adult: Sailor-mouth - swears, crude, 18+"`. The `CrudeRule` from step 2 becomes `SailorMouthRule`.
- **Screen text is starred**, as in step 6.
- **Clubs can switch it off.** Add a club rule in Setup → Club rules, "Allow
  Sailor-mouth commentary", on by default. While it is off:
  - Sailor-mouth does not appear in the Tone dropdown.
  - A saved Sailor-mouth setting reads as Enthusiastic, so nobody hears it by surprise.

  Put it in the same place the other club rules live, with a test that covers both
  halves. This is step 5b.
- **Premium** (added 15 Nov 2026): Sailor-mouth, Pirate, Town crier and Villain
  carry `Premium: true` and show "(Premium)" in the dropdown. For now that is only a
  label, because the app has no plan or licence check to lock them behind.
