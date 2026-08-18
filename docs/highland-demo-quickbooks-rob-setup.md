# QuickBooks setup for the Highland demo (instructions for Rob)

Date: 2026-08-17. Owner of the steps below: Rob. Time: about 15 minutes. Nothing here touches the LoamPass company books; everything happens in a free Intuit sandbox company.

## What we need from you

1. An Intuit Developer app named "RidePass" created under your Intuit login (the same login you use for the loampass.com QuickBooks). Creating it under your login makes LoamPass the owner of the app and its sandbox.
2. The app's sandbox Client ID and Client Secret.
3. Our stage callback URL registered on the app.
4. The sandbox company renamed for Highland and Dave invited to it as a company admin.

## Steps

1. Go to https://developer.intuit.com and Sign In with your Intuit login (top right).
2. Open the Dashboard (My Apps) and click "Create an app". Choose "QuickBooks Online and Payments". Name: `RidePass`. Scope: check "Accounting" (`com.intuit.quickbooks.accounting`). Create.
3. In the new app, open "Keys & credentials" under Development (sometimes labeled "Keys & OAuth"). You will see the Development (sandbox) Client ID and Client Secret. Send both to Dave through a password manager share (1Password / Bitwarden) or read them to him on a call. Please do not email or text them.
4. On that same page, under Redirect URIs, add exactly:
   `https://stage.ridepass.io/api/QuickBooks/Callback`
   and Save. (Later, when this goes to production, we will add `https://ridepass.io/api/QuickBooks/Callback` under the Production keys. Not needed for the demo.)
5. Sandbox company: in the developer Dashboard open "Sandbox" (left nav, or under API Docs & Tools). Intuit created a US sandbox company for you automatically. Click "Go to company" to open it. It is a fully working QuickBooks Online with a "Sandbox" banner.
6. Rename it so it looks like Highland's books: gear icon (top right) > Account and settings > Company > Company name = `Highland Mountain Bike Park (RidePass demo)`. Save.
7. Invite Dave so he can connect RidePass to it and set up the chart of accounts without your password: gear icon > Manage users > Add user > User type "Company admin" > email `djhoen@gmail.com` > Send invite. Dave accepts the invite with his own Intuit account.
   If, when Dave clicks "Connect to QuickBooks" in RidePass, the sandbox company does not appear in Intuit's company picker, the fallback is that you click Connect once from your login (60 seconds, we can screen-share). The connection then stays live on its own (Intuit refresh tokens last 100 days and RidePass rotates them).

That is everything on your side. Dave will create the park-shaped chart of accounts (Lift Ticket Revenue, Season Pass Revenue, Food & Beverage Revenue, Bike Shop Sales, Gift Card Liability, Tips Payable, Stripe Clearing, Due from RidePass, Stripe Fees, RidePass Fees, and so on) inside the sandbox once he has access.

## What happens next on our side (Dave)

- Put the sandbox Client ID / Secret / redirect URI into the RidePass staging config and restart the stage services.
- Connect the `highland` demo tenant on `highland.stage.ridepass.io` to the sandbox company, map each RidePass revenue / liability / asset / expense slot to the accounts above, and back-post the last 30 to 45 days of Highland's demo sales as one journal entry per business day.
- Verify the entries and the Profit & Loss report inside the sandbox before the demo.

## Later, for real customers (not for the demo)

Production keys on the same app need the app's Production settings filled in (end-user license agreement URL, privacy policy URL, host domain, launch URL) and Intuit may ask for a short compliance questionnaire before issuing them. We will handle that when Highland or another park wants to connect their real QuickBooks.
