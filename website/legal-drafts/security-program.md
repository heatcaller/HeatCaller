> **DRAFT for review by a licensed attorney - not legal advice, not in force.**
> Written 15 Nov 2026 for HeatCaller LLC (plans/children-privacy-and-ip-plan.md, item 2.7). A written information security program in the shape COPPA §312.8 asks for (as amended in 2025) if HeatCaller LLC collects personal information from children under 13 online. The build is designed so that it doesn't (juniors' own words are typed at the club's desk), so counsel may say this is optional; it is good practice either way, and the UK Children's Code and state laws expect something like it. Points marked **[CHECK]** need a fact or a decision.

# HeatCaller information security program

**Owner:** HeatCaller LLC
**Program coordinator:** ______________________ (name), ______________________ (email)
**Adopted:** ______________ **Last reviewed:** ______________ **Next review due:** ______________

## 1. Purpose and scope

This program protects the confidentiality, security and integrity of personal information about drivers, many of them children, that HeatCaller LLC holds or processes. It covers:

- **The relay** (hosted on Fly.io in the US): results and live pages, check-ins and sign-ups, drivers' requests and crew copies, follow and push subscriptions, family links, cached announcer lines, and race video links.
- **The HeatCaller voice and online commentary**: lines and race facts sent through the relay to ElevenLabs and Anthropic.
- **Race video storage** (Cloudflare R2) and anything else HeatCaller LLC hosts.
- **Support material** clubs send us (Save Support Info files, emails, screenshots).
- **HeatCaller LLC's own accounts and devices** used to build and run the above.

It does not cover the club's own computer, which the club controls. The desktop app's defaults (encryption of keys, PINs and backups; freeze reports deleted after 14 days) are part of section 5 because we design them.

## 2. Coordinator

The coordinator named above is responsible for this program: keeping it current, running the annual risk assessment, keeping the records in section 9, and being the contact for security and privacy questions. If the coordinator changes, the new name is written above the same day.

## 3. What we hold about children

| Data | Where | Children's? |
|---|---|---|
| Shortened names, results, schedules | Relay | Yes. Juniors' names always shortened; pages naming a junior marked noindex |
| Check-ins and sign-ups | Relay | Yes, briefly (12 hours / 14 days) |
| Crew requests | Relay | 13-17 crew names only; under-13s send none. Juniors send no notes or about line from a phone |
| Current crew and notes copy | Relay | Yes, for the driver's own phone |
| Follows, push subscriptions, family links | Relay | Juniors only through family links; expire after 180 days unused |
| Junior phone sign-ins | Relay | Expire after 30 days |
| Spoken lines naming a driver | Relay cache, ElevenLabs | Yes, short names; per-club only |
| Race facts for commentary | Anthropic (via relay) | Yes, short names; juniors' notes only with recorded consent |
| Race video | R2 / YouTube | Yes, if the club records |
| Support files | Our email | Should not, but may (names are removed on a best-effort basis) |

The Phase 1-2 changes landed on 16 Nov 2026 (PR #236) and this table describes them. Check it again at each annual review.

## 4. Annual risk assessment

At least once a year, and after any significant change (a new feature that touches driver data, a new vendor, an incident), the coordinator:

1. Lists what children's information is held, where, by whom, and for how long (section 3).
2. Identifies the risks to it - for example: a relay breach; a stolen admin credential; a vendor keeping data longer than promised; a club PIN or check-in code guessed or shared; a family link forwarded; a support file with names in it; a public page indexed by a search engine.
3. Rates each risk (likelihood and impact) and notes the safeguard in place.
4. Decides what to change, who does it and by when.
5. Writes it up and keeps it (section 9).

The first assessment is the DPIA (website/legal-drafts/dpia.md).

## 5. Safeguards

**Access.**
- Only the coordinator and named people have admin access to Fly.io, R2, the relay's admin pages, the ElevenLabs and Anthropic accounts, and the code repositories. List them: ______________________.
- Every such account uses a unique password and two-factor authentication.
- Relay secrets (API keys, signing keys) live in the host's secret store, never in the code. Checked 16 Nov 2026: Fly.io holds two secrets, RELAY_CONFIG_JSON (the clubs' token hashes and settings) and RELAY_BRAIN_KEY. Neither is in git.
- Access is removed the same day someone no longer needs it.

**The relay.**
- HTTPS only.
- Each club's data is kept apart by its club key; one club cannot read another's.
- Driver phone access is by personal PIN; junior sign-ins expire after 30 days.
- Rate limits on PIN entry. Checked 16 Nov 2026: a club's PIN list closes after 50 wrong PINs within 10 minutes, counted across every address. It closes for 10 minutes the first time, and longer each time after that, up to 12 hours (relay/src/pinguard.ts). [CHECK: whether family-link and crew codes have a limit of their own.]
- The retention timers in Part 2 of the Terms run automatically, and are tested.
- Logs do not record notes, PINs or email addresses. Checked 16 Nov 2026: the relay's own log lines name the club and the kind of document, never what is in it. Fly.io's edge keeps its own request logs, with IP addresses and page addresses, under Fly's retention.
- Backups of relay data have the same retention. Checked 16 Nov 2026: Fly takes a daily snapshot of the relay's encrypted volume (relay_data) and keeps 5. Anything deleted on the relay is therefore gone from every Fly copy within about 5 days, and the retention schedule and Forget should say "within 5 days" for those copies. The other copy is backup.cmd's relay-data.tgz on the operator's PC, which is not encrypted (counsel brief 4.3). Keep it no longer than the newest backup needs, in an encrypted folder.

**Vendors.**
- ElevenLabs: each request asks not to log or keep the line where the account allows it; Offline voices for juniors keeps lines naming juniors on the club's computer.
- Anthropic: the commercial API only. Its terms say inputs are not used for training.
- Each vendor's data terms are read before use and re-read at each annual review; written answers are filed in website/legal-drafts (vendor-letters.md).

**The desktop app (by design).**
- Keys and PINs encrypted with Windows DPAPI.
- Backups encrypted with the club's backup password; no cloud-synced backup without one.
- Freeze reports deleted after 14 days.
- Forget this driver and Export this driver's data.

**Development.**
- Changes that touch driver data are reviewed against this program and Part 2 of the Terms before release.
- Dependencies are kept up to date; security advisories for the relay's packages are watched.
- Test and demo data never uses real children's names.

**Support.**
- Support files and emails containing driver data are deleted once the issue is closed, and at most 90 days after. [CHECK the period]
- Support files are not shared outside HeatCaller LLC.

## 6. Service providers

Before giving a provider access to children's information, we:

1. take reasonable steps to choose one able to keep it secure;
2. get its written assurance (its terms, a DPA, or a letter) that it keeps it confidential and secure and uses it only to provide its service to us; and
3. note it in section 3.

Current providers: Fly.io (relay hosting), Cloudflare (R2 video storage), ElevenLabs (speech), Anthropic (commentary). [CHECK each one's DPA.]

## 7. Incidents

If children's or other drivers' information may have been accessed without permission:

1. **Contain** it (rotate keys, close access, take the affected endpoint down).
2. **Record** what happened, when, what data and which clubs.
3. **Assess** whether notification is required - to clubs, parents, regulators (state breach laws; the FTC; the UK ICO within 72 hours if UK personal data is affected). [CHECK with counsel at the time.]
4. **Tell the affected clubs** promptly, so they can tell their drivers and parents.
5. **Fix** the cause, and add it to the next risk assessment.

## 8. Training

Anyone with admin access reads this program and Part 2 of the Terms when they get access, and at each annual review.

## 9. Records

Keep for at least three years [CHECK]:

- each annual risk assessment and what was done about it;
- the list of people with admin access, and changes to it;
- vendor assurances;
- incidents and how they were handled;
- each review of this program, with the date and the coordinator's name.

## 10. Review

The coordinator reviews this program at least once a year and whenever the business or the law changes in a way that affects it, and records the date above.
