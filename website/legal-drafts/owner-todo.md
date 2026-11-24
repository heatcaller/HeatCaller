> Written 16 Nov 2026 for HeatCaller LLC. The owner's five jobs from the children's privacy and IP plan, one at a time. Every fact the code or the accounts could answer has been filled into the drafts already; what is left here needs Asher's own details, signature, payment or email. Not legal advice.

# Owner to-do

Do them in this order. 1 and 4 are done, 3 is registered and its page is live, 2 is ready to sign, and 6 (New York publication) is under way. 1 and 2 take about 15 minutes each, 3 about 20 minutes, and 5 is waiting on replies.

## 1. Name the security coordinator (security-program.md) - DONE 23 Nov 2026

Asher is the coordinator at support@heatcaller.com, adopted 23 Nov 2026, next review 23 Nov 2027 (in the calendar). Support files are kept at most 90 days, security records three years, and family-link codes and crew picks have a wrong-guess limit (built 23 Nov 2026; the relay needs deploying for it to be live). There is no separate crew code: crew picks use the driver's badge code, which is counted on crew picks only - say if you want it counted on its other routes too. Still blank in the draft: the list of people with admin access (section 5), which is just Asher for now.

The original steps, for the record:


1. In the header, write the coordinator's name and email. For a one-person company that is you, at a mailbox you read (support@heatcaller.com works). Then write today's date as **Adopted**, and a year from today as **Next review due**.
2. Decide the three **[CHECK]** points left:
   - how long support files are kept (the draft says 90 days);
   - how long security records are kept (the draft says three years);
   - whether family-link and crew codes need a wrong-guess limit like the PIN one.
3. Put a reminder in your calendar for the review date.

## 2. Sign the founder IP assignment (founder-ip-assignment.md) - READY TO SIGN

Filled in 23 Nov 2026: both addresses ("506 Pinyon Dr, Schenectady, NY 12303"), formed 26 September 2026 (New York DOS ID 8031449), title Managing Member, Porkbun for the domain and Google for email, no payment or app-store accounts yet, and no employer claims anything. 24 Nov 2026: the consideration is the US$500 opening deposit as Asher's capital contribution, for his membership interest (the assignment goes in with it); the email is a plain Gmail account, not Google Workspace. Print founder-ip-assignment.pdf twice, then date and sign both copies (once as Founder, once as Managing Member) and keep one with the company records.

The original steps, for the record:


The repositories and the main accounts are already listed. You fill in:

- **your address and the company's address.** The company's can be the registered address from the New York filing.
- **the date the LLC was formed**, from the New York Articles of Organization.
- **the consideration.** Decided 24 Nov 2026: part of the capital contribution with the US$500 opening deposit, for the membership interest. Counsel can confirm it matches the operating agreement.
- **past employers.** Does any current or past employer's contract claim things you make in your own time? If one might, show that clause to counsel before signing.
- **the accounts still missing:** domain registrar, email provider, payment or app-store accounts.
- **your title.** "Managing Member" if the LLC is member-managed.

Print two copies, sign both (once as yourself, once for the company), and keep one with the company records. Counsel can look at it first; it is item 6 on the brief's reading list.

## 3. Register the DMCA agent, then publish the takedown page (copyright-takedown-policy.md) - REGISTERED 23 Nov 2026

Steps 1 to 4 are done: designation **DMCA-1087259**, agent "Copyright Agent, HeatCaller LLC", 506 Pinyon Dr, Schenectady, NY 12303, support@heatcaller.com (the LLC's own address was chosen). Part A of the policy has the same details. Left:

- **The page:** live at https://heatcaller.com/copyright since 23 Nov 2026 (heatcaller-site PR #38), linked as Copyright in the footer. Done.
- **The renewal:** put the expiry date from the confirmation (about November 2029) in your calendar.
- **One-item removal (step 6):** built 23 Nov 2026 on branch `claude/takedown`: `takedown-find`, `takedown`, `takedown-restore` and `takedown-list`, run over `fly ssh console` (steps in the relay hosting guide, "Copyright takedowns"). It works once that branch is merged and the relay deployed; until then a takedown means asking the club. Listen audio and captions still mean asking the club to switch Listen off.
- **The notice log:** `takedown-list` keeps each takedown with its club, item, your notice reference, dates and any restore, and the admin audit log has each action. It deliberately keeps nothing about who sent the notice, so keep the notices themselves (and the URL each named) in your own mail folder, filed by the same reference.

The original steps, for the record:


1. Go to <https://dmca.copyright.gov/> and create an account (free).
2. Register **HeatCaller LLC**, with the name exactly as on the New York filing. List the names it is known by: "HeatCaller", `heatcaller.com` and `heatcaller-relay.fly.dev`.
3. Name the agent: "Copyright Agent, HeatCaller LLC". Give a mailbox you read (create copyright@heatcaller.com, or use support@). The postal address is published in the directory. The LLC is at your home, so choose one of these:
   - **Your home address.** It costs nothing. If you gave the same address to New York as the LLC's address for service of process, it is probably public already in the state's business entity search, which you can check. The cost is a public link between your name, your home and every takedown notice.
   - **A registered-agent or virtual-mailbox service with a street address.** These usually cost about US$50-150 a year. It also lets you move the LLC's service-of-process address off your home (a change filed with New York's Department of State, with a small fee), which keeps your home off both lists. This is the usual choice for a home business.
   - **A P.O. box is probably not accepted.** The Copyright Office expects a street address. Check the form's own instructions.

   Recommended: the mailbox service, put on both filings at once.
4. Pay the $6 and save the confirmation. The registration expires after 3 years, so put the renewal date in your calendar.
5. Copy the same address and email into Part A of the policy, so the page and the directory match. Then put Part A on heatcaller.com and link it from the site footer.
6. Before going live: the relay can't yet remove one item for one club without deleting the whole club. Until it can, a takedown means asking the club, or a hand edit over `fly ssh`. Claude can build an admin removal if you want it.

## 4. Upload the VLC source and fill in the link - DONE 16 Nov 2026

The sources are on the public HeatCaller-releases repository as the `third-party-source` release: <https://github.com/heatcaller/HeatCaller-releases/releases/tag/third-party-source>.

- `vlc-3.0.24.tar.xz`: its SHA-256 matches VideoLAN's.
- `libvlcsharp-3.10.1.tar.gz`: from the 3.10.1 tag.

The release is marked as a pre-release, so the app's update check never treats it as an update; v0.63.0 is still the latest release. The About box and LibVLC-plugins.txt link to it from the next build on. If a later build changes the VLC or LibVLCSharp version, upload that version's source the same way.

## 5. Send the vendor letters (vendor-letters.md)

1. Read both letters and change anything that isn't how you would put it.
2. Send each one through the company's "Contact sales" form, or by email if you have a contact. Send them from your HeatCaller address.
3. Done 24 Nov 2026: ElevenLabs' terms, DPA and docs were read, and the ElevenLabs letter is cut to the four questions they leave open (the table in vendor-letters.md has the rest). Training opt-out done 24 Nov 2026 on the HeatCaller and development accounts (Data use turned off), so the letter's "We have opted out of training" is true.
4. When answers come back, paste each under **Answer filed** with the date and who it was from. If an answer changes anything, the Terms Part 2 and section 7 of driver-crew-children.md need a matching edit; Claude can do that from the filed answer.

What to expect, from the vendors' own documentation (16 Nov 2026):

- **ElevenLabs:** contact requested 24 Nov 2026 through its contact form; take the letter's four questions to that conversation. Zero Retention Mode is for enterprise customers only (confirmed in the API reference, 24 Nov 2026). Training on our content is on by default until we opt out. All Default voices (Daniel among them) stop working on 31 Dec 2026.
- **Anthropic:** zero data retention is granted by sales, for one organization at a time. Requested 24 Nov 2026 through Anthropic's "Contact us" form (not sales); if it is passed back to sales, ask again at anthropic.com/contact-sales.

## 6. New York LLC publication (Limited Liability Company Law §206) - UNDER WAY

New York's record for HeatCaller LLC (DOS ID 8031449, checked 23 Nov 2026) shows only the Articles of Organization, filed 26 September 2026. A new New York LLC must publish a notice of its formation once a week for six weeks in two newspapers the county clerk names, then file a Certificate of Publication with the Department of State, within 120 days of formation: by about **24 January 2027**. An LLC that misses it has its authority to do business suspended until it files.

1. The publication notice is ready to go (Asher, 23 Nov 2026). Six weekly runs need to start by mid-December to finish in time.
2. When the newspapers send their affidavits of publication, file them with the Certificate of Publication (form DOS-1708) and the fee.
3. Check the entity record afterwards: the filing history should list the certificate.

Also on the record: the first **biennial statement is due 30 September 2028**.
