> **DRAFT for review by a licensed attorney - not legal advice.**
>
> **Updated 23 Nov 2026: the agent is registered.** Asher registered HeatCaller LLC with the Copyright Office's DMCA Designated Agent Directory: designation **DMCA-1087259**, agent "Copyright Agent, HeatCaller LLC", at the LLC's address with support@heatcaller.com. Part A below has those details, and the page is live at https://heatcaller.com/copyright (heatcaller-site PR #38, merged 23 Nov 2026). The two policy choices left open are filled in as the draft suggested; counsel may still change them.
> Written 15 Nov 2026 for HeatCaller LLC (plans/children-privacy-and-ip-plan.md, item 3.6). Part A is a page for heatcaller.com. Part B is the owner's checklist for registering a designated agent with the US Copyright Office, which Asher files himself. Points marked **[CHECK]** need a fact or a lawyer's wording before the page goes live. The page must not go live until the agent is registered: the safe harbour in 17 U.S.C. §512(c) depends on it.

# Copyright and takedown policy

## Part A: the page for heatcaller.com

### Copyright on HeatCaller's online pages

HeatCaller LLC ("we") runs the HeatCaller relay, which hosts pages that RC racing clubs publish: results, standings, schedules, photos, sponsor slides, logos, race video, captions and audio. The clubs choose what goes on those pages, and each club promises us that it has the right to publish it (Terms, Part 1, section 7.4).

We respect copyright, and we expect clubs to as well. If you believe something on a HeatCaller page infringes your copyright, send us a notice as described below and we will deal with it under the Digital Millennium Copyright Act (17 U.S.C. §512).

### How to send a notice

Send it to our designated agent:

> **Copyright Agent, HeatCaller LLC**
> 506 Pinyon Dr
> Schenectady, NY 12303
> Email: support@heatcaller.com

Your notice must include:

1. Your physical or electronic signature, as the copyright owner or someone authorised to act for them.
2. The copyrighted work you say is infringed (or, for several works, a representative list).
3. The material you say infringes, and enough information for us to find it - the page address (URL) is best.
4. Your name, postal address, telephone number and email address.
5. A statement that you have a good faith belief that the use is not authorised by the copyright owner, its agent or the law.
6. A statement that the information in the notice is accurate and, under penalty of perjury, that you are the owner or authorised to act for the owner.

A notice missing any of these may not be valid, and we may ask you to complete it. Someone who knowingly misrepresents that material is infringing may be liable for damages (17 U.S.C. §512(f)).

### What we do with a notice

- We remove the material, or disable access to it, promptly.
- We tell the club that published it, and pass on a copy of the notice, with the sender's telephone number and postal address taken out.
- We keep a record of the notice.

### Counter-notice

If a club believes material was removed by mistake or misidentification, it can send our agent a counter-notice with:

1. Its physical or electronic signature.
2. The material removed and where it appeared before it was removed.
3. A statement under penalty of perjury that it has a good faith belief the material was removed by mistake or misidentification.
4. Its name, address and telephone number, and a statement that it consents to the jurisdiction of the federal district court for its address (or, outside the US, any judicial district where HeatCaller LLC may be found), and will accept service of process from the person who sent the notice.

We forward a valid counter-notice to the person who sent the original notice. Unless they tell us within 10 business days that they have filed a court action, we restore the material in 10 to 14 business days after we receive the counter-notice.

### Repeat infringers

We will, in appropriate circumstances, end the online services (the relay, the HeatCaller voice and online commentary) for any club or user who repeatedly infringes others' copyright. As a guide:

- We keep a record of each valid notice against each club.
- A club with **three** valid notices that were not successfully countered within any 12 months is a repeat infringer, and its relay account is closed.
- We may act sooner for a clear or wilful case.
- A club whose account is closed may not open a new one without our agreement.

### Other complaints

For trademark complaints, a driver's or parent's privacy request, or anything else about a club's page, email support@heatcaller.com. These are not handled under this policy.

## Part B: owner's checklist for the designated agent

Done 23 Nov 2026: designation **DMCA-1087259**. The first seven boxes are ticked; the page is live at https://heatcaller.com/copyright since 23 Nov 2026.

The Copyright Office runs an online directory of designated agents. Asher files this himself.

- [x] Create an account on the DMCA Designated Agent Directory: <https://dmca.copyright.gov/>. **[CHECK]** the current address and fee (checked 16 Nov 2026: the account is free, and each designation, renewal or change costs US$6. Confirm on the payment page).
- [x] Register HeatCaller LLC as the service provider, with its legal name exactly as on the New York filing.
- [x] List every name the service is known by: "HeatCaller", and the relay's public domain(s). Checked 16 Nov 2026: the relay answers only at heatcaller-relay.fly.dev, with no custom domain on the Fly app. List that and heatcaller.com. If a heatcaller.com subdomain is pointed at the relay later, change the designation that day ($6).
- [x] Name the agent. It can be a role ("Copyright Agent") with Asher's details or a registered agent service. The postal address is published, so consider a business address or a registered-agent address rather than a home address.
- [x] Give the agent's email and phone. Use a mailbox someone reads, such as copyright@heatcaller.com.
- [x] Pay the fee and save the confirmation.
- [x] Put the same agent details on the heatcaller.com page (Part A), so the page and the directory match exactly.
- [ ] Set a reminder: the designation **expires after three years** unless renewed (about November 2029; the exact date is on the confirmation). Renew before the expiry date shown on the confirmation. Update it straight away if the address or email changes.
- [ ] Set up a simple log for notices, counter-notices and action taken (date, club, URL, outcome). It is the evidence for the repeat-infringer policy.
- [x] Check how to remove one item from the relay for one club. Built 23 Nov 2026 (branch `claude/takedown`): the relay admin commands `takedown-find`, `takedown`, `takedown-restore` and `takedown-list` take down one logo, sponsor, track picture, achievement artwork, race video, race clip, crew voice sample or garage photo for one club within seconds, keep it down when the club publishes again or deletes everything, and put it back after a counter-notice. `takedown-list` counts each club's uncountered notices over 12 months and flags three (it never closes a club). Steps are in the relay hosting guide, "Copyright takedowns". Listen audio and captions are not covered one at a time: ask the club to switch Listen off, or pause the club. It works once the relay is deployed with it.
- [ ] Add a link to the page from the heatcaller.com footer and from the Terms (Part 1, section 7.4 already says we may remove content on a complaint).
