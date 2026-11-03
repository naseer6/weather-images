# Submission checklist

For exact account setup and commands, follow [GitHub and Azure setup](github-and-azure.md).

- Deploy with `deploy.ps1` and run the smoke test against Azure.
- Confirm the station-count discrepancy with the assessor: Buienradar supplied 39 distinct stations. The app generates 50 images by reusing 11 stations with different photos and unchanged weather; this is not 50 distinct stations.
- Commit `docs/api.deployed.http` with the real URL but no key.
- Push all source to the correct GitHub repository.
- Add Hijdra to the repository/organization/project as applicable.
- Privately give the assessor the function key and URL.
- Check the final repository contains no secrets or local settings.

Draft email (send yourself after completing the checklist):

To: mark.hijdra@wearetriple.com

Subject: Cloud Computing assignment — Naseer Ahmed Arsalan (722589)

Dear Mark,

I have finished my Azure weather image assignment.

Repository: [insert repository URL]

API: [insert deployed URL]

Name: Naseer Ahmed Arsalan

Student number: 722589

AI usage: Yes. AI assisted implementation; I have reviewed the code and learned from the assignment.

The request endpoint requires a function key: [provide privately]. API examples are in docs/api.deployed.http.

The Buienradar feed supplied 39 distinct stations during development. This implementation generates 50 images by reusing 11 stations with different photos while preserving their weather readings. [Include the assessor's agreement on this approach if obtained.]

Kind regards,
Naseer Ahmed Arsalan
