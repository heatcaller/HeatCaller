> Written 16 Nov 2026 for HeatCaller LLC. The owner's five jobs from the children's privacy and IP plan, one at a time. Every fact the code or the accounts could answer has been filled into the drafts already; what is left here needs Asher's own details, signature, payment or email. Not legal advice.

# Owner to-do

Do them in this order. 4 is done. 1 and 2 take about 15 minutes each, 3 about 20 minutes, and 5 is waiting on replies.

## 1. Name the security coordinator (security-program.md)

1. In the header, write the coordinator's name and email. For a one-person company that is you, at a mailbox you read (support@heatcaller.com works). Then write today's date as **Adopted**, and a year from today as **Next review due**.
2. Decide the three **[CHECK]** points left:
   - how long support files are kept (the draft says 90 days);
   - how long security records are kept (the draft says three years);
   - whether family-link and crew codes need a wrong-guess limit like the PIN one.
3. Put a reminder in your calendar for the review date.

## 2. Sign the founder IP assignment (founder-ip-assignment.md)

The repositories and the main accounts are already listed. You fill in:

- **your address and the company's address.** The company's can be the registered address from the New York filing.
- **the date the LLC was formed**, from the New York Articles of Organization.
- **the consideration.** "US$1 and other good and valuable consideration" is the usual short form. Ask counsel whether it should be a capital contribution under your operating agreement.
- **past employers.** Does any current or past employer's contract claim things you make in your own time? If one might, show that clause to counsel before signing.
- **the accounts still missing:** domain registrar, email provider, payment or app-store accounts.
- **your title.** "Managing Member" if the LLC is member-managed.

Print two copies, sign both (once as yourself, once for the company), and keep one with the company records. Counsel can look at it first; it is item 6 on the brief's reading list.

## 3. Register the DMCA agent, then publish the takedown page (copyright-takedown-policy.md)

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
3. Before sending the ElevenLabs one, open its DPA (reported at elevenlabs.io/dpa). It may already answer the caching, location and logging questions.
4. When answers come back, paste each under **Answer filed** with the date and who it was from. If an answer changes anything, the Terms Part 2 and section 7 of driver-crew-children.md need a matching edit; Claude can do that from the filed answer.

What to expect, from the vendors' own documentation (16 Nov 2026):

- **ElevenLabs:** Zero Retention Mode is for enterprise customers only, which probably means an enterprise plan.
- **Anthropic:** zero data retention is granted by sales, for one organization at a time.
