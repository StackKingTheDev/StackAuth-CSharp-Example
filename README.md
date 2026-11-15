# StackWorkShop Auth: C# example

A complete WinForms app (.NET 8, Visual Studio solution) that uses [StackWorkShop Auth](https://auth.stackworkshop.com) for sign-in, licensing and protected data. Download it, press **Start**, and it connects to our public **StackWorkShop** demo application with every feature turned on.

## Run it

1. Install [Visual Studio 2022](https://visualstudio.microsoft.com/) with the **.NET desktop development** workload (or the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)).
2. Download this repository (**Code → Download ZIP**) or `git clone` it.
3. Open `StackAuthExample.sln` and press **F5**.

Command line: `dotnet run --project StackAuthExample`

## Try the demo

| Account | Username | Password | What you see |
|---|---|---|---|
| Lifetime member | `demo_member` | `DemoMember!2026` | The member home page |
| Admin (staff) | `demo_admin` | `DemoAdmin!2026` | Everything above plus the **Admin** tab (read-only) |

The sign-in window has a button for each. Both are *shared demo accounts*: anyone can use them on any PC, and nobody can change them. You can also **register** your own account (free mode is on in the demo app) to try the things demo accounts can't do: change password, 2FA, user variables, devices, HWID reset.

## What's inside

**Sign-in window** (`LoginForm.cs`)
- `init()` at startup: app status, version check, announcement, update download on `UPDATE_REQUIRED`
- **Sign in** (with a 2FA prompt when the account has it), **Register**, **License key** sign-in
- **Remember me**: the refresh token is stored with Windows DPAPI and swapped for a new session at the next start

**Home window** (`MainForm.cs`)
- Profile: avatar, username, email, level, lifetime/expiry, member since, last sign-in, 2FA, demo/staff badges
- **Variables**: public, secured and level-gated variables, and **Fake mode** (read the same variable without a login and get the fake value)
- **User variables**: per-member storage
- **Files & updates**: load a protected file into memory (as you would `Assembly.Load` a DLL) or stream it to disk; the SHA-256 is checked against the signed response. Update check (link or uploaded file)
- **Vault**: encrypt/decrypt strings with the licensed string vault
- **Account**: redeem a key, change password, 2FA on/off, self-service HWID reset, devices (sign out one or all others), app log, Discord relay
- **Admin** (`AdminPanel.cs`, staff only): stats, members (ban/unban, reset HWID, add days), license keys (create), recent activity

Every button is one SDK call, so you can copy the handler you need.

## Use your own application

1. Sign in at [auth.stackworkshop.com/dashboard](https://auth.stackworkshop.com/dashboard) and create an application.
2. In `StackAuthExample/AppConfig.cs`, set `AppId` and `AppSecret` from your app's **Overview** tab, and `Version` to your app's current version.
3. Delete the demo helpers at the bottom of `AppConfig.cs` and adjust the variable and file names.
4. For the **Admin** tab, set **Settings → In-app staff panel → Staff level** (for example 10) and give your staff members that level.

`StackAuth.cs` is the official SDK (one file, no packages). The latest version is always at [auth.stackworkshop.com/sdk/StackAuth.cs](https://auth.stackworkshop.com/sdk/StackAuth.cs).

## Security notes

- The demo app's secret is in this repository **on purpose**. Your own secret ships inside your build, so protect the build (obfuscation, **Startup hash check**, **Dependency file check**) and rotate the secret from the dashboard if it leaks.
- The SDK signs every request and verifies the signature on every response, so a fake "login OK" from a proxy, hosts-file redirect or patched DNS is rejected.
- The HWID (`Hwid.cs`) is a SHA-256 of the Windows MachineGuid, machine name and user name; the server stores only a salted hash of it.

Docs: [auth.stackworkshop.com/docs](https://auth.stackworkshop.com/docs/) · Other examples: [PHP](https://github.com/StackKingTheDev/StackAuth-PHP-Example) · [Node](https://github.com/StackKingTheDev/StackAuth-Node-Example)

MIT licensed.
