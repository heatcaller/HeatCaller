> **For review by a lawyer - not legal advice.**
> Written 15 Nov 2026 alongside the cookie policy (relay `/c/<club>/cookies`, source `relay/src/cookies.ts`) and Part 3 of the in-app terms (`HeatCaller/HeatCaller/Assets/Legal/HeatCaller-Terms.md`). The policy and the code were changed to fix what could be fixed without a lawyer; these are the questions that need one.

# Cookie policy: open questions for counsel

## What was changed after the first review

- Two false statements corrected: the pages **do** keep offline copies of the club's public pages (service worker cache `hc-pages-<build>`), and they **do** send anonymous usage counts (`relay/src/pagecounts.ts`, 90 days).
- Every item the pages keep in a browser is now named, with what it is for and how long it lasts. A relay test fails if the page code keeps something under a name the policy does not list.
- Two items that were not needed for anything the visitor asked for were changed in the code:
  - `hg-visits-<club>`, a visit count for the home-screen offer, is no longer kept and is deleted where found. The offer now waits 45 seconds on the page, counted in memory.
  - `hc-clubs`, the list of clubs opened on the phone, now records only clubs where a driver signed in on Me, and a club comes off it when its last driver signs out.
- Added: who the policy is from (HeatCaller LLC), a version and change history, children, push services, IP addresses and how long they are held (in memory, ten minutes at most), Fly.io, YouTube loading only on a tap, rights, US transfer, complaint to a regulator, GPC/Do Not Track.
- Corrected overpromises: the check-in page keeps the PIN typed for the open tab; the manager page keeps a key derived from the manager PIN for the open tab; sign-out keeps the manager phone's key and id; clearing site data does not remove the push address on the relay; blocking storage does break some things.
- The policy moved out of the terms. Part 3 now points to it and sets out the club's part, so editing the policy no longer makes every club accept the terms again.

## Questions

These are also in the counsel brief (`website/legal-drafts/counsel-brief.md`, branch `claude/children-privacy-ip`), under its own numbers. Brief numbers are in brackets; use those with counsel.

1. **Consent (UK PECR, EU ePrivacy Art. 5(3)).** [brief 19] The policy says no consent is needed because each item is needed for something the visitor asked for. Please confirm for:
   - the offline copies of public pages (`hc-pages-`), which the helper keeps on every visit, not only for people who install the app;
   - "seen" markers (`hg-spseen-`, `hc-guide-done:`, `hc-updates-done:`, `hg-me-seen-`);
   - the 45-second wait for the home-screen offer, which keeps nothing but still shows a prompt;
   - the voter id `hg-voter-`.
   If any needs consent, which mechanism is acceptable for a mostly-child audience?
2. **COPPA.** [brief 1] Three persistent identifiers can come from under-13s: the voter id, the push address, and the night code kept on the phone. Does each fall under "support for the internal operations" (16 CFR 312.2, 312.5(c)(7))? Is the push address used to send alerts the child turned on "contacting" the child?
3. **UK Children's Code.** [brief 20] Is the policy's children section clear enough for a child, or is a separate child-friendly version needed? Are the defaults high-privacy enough?
4. **Who is responsible.** [brief 3] The policy says HeatCaller LLC decides what the pages keep in a browser and so the policy is ours. Is HeatCaller LLC a controller for this storage, and the club a controller for what it publishes? Does Part 3's split of duties between club and HeatCaller hold up?
5. **Postal address, EU/UK representative.** [brief 21] The policy gives "HeatCaller LLC, New York, USA" with no street address. Is a full postal address required? Does HeatCaller need an Art. 27 representative in the EU or UK, given clubs there can use it?
6. **Transfers.** [brief 18] The policy says the relay is in the US. Is a transfer mechanism needed and should it be named?
7. **Legal basis wording.** [brief 22] The policy gives the reasons in plain words ("needed for something the pages do for you"; "we need to keep the pages working well") rather than naming GDPR Art. 6(1)(b)/(f). Is that enough?
8. **Indemnity.** [brief 23] Part 3 sets out the club's duties but has no indemnity. During a free beta, should it have one?
9. **The privacy notice is not public.** [brief 24] Part 2 of the terms (the privacy notice) is shown only in the app, to clubs. The cookie policy tells visitors to "ask the club, or email us" about driver information. Should Part 2, or a version for drivers, be published on the relay as well?
10. **GPC.** [brief 25] The policy treats a Global Privacy Control signal as an opt-out of sale or sharing, which HeatCaller never does. Is anything more needed under CCPA/CPRA or the state laws that require honouring GPC?
11. **YouTube.** [brief 26] The policy says loading the player sends the visitor's address to Google. Is a separate notice needed at the moment of the tap?
