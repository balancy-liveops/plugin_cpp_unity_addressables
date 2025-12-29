# Balancy Addressables + CDN Hosting

This module simplifies your workflow with Unity Addressables and offers free hosting on Balancy's global CDN servers.

## What Are Addressables and Why Use Them?

**Addressables** are Unity's official solution for dynamic content management. They allow you to load and manage assets (like textures, audio, prefabs, and more) at runtime — which means you don't need to include everything in your initial game build. This leads to **smaller app sizes**, **faster updates**, and **more flexible content delivery**.

Balancy takes this even further by offering a **complete Addressables hosting solution**:

- Host your Addressable Asset Bundles on **Balancy's global CDN**.
- Deliver assets dynamically to players **on demand**, based on their in-game behavior or feature usage.
- Manage asset versions and bundle configurations from a single dashboard — **no app updates required**.

With Balancy's system, you can define which content is downloaded automatically, which bundles are pre-cached, and which assets are updated for different game versions — all without changing your core app logic.

> 💡 **Free Until August 2025**
> You can use Balancy's Addressables Hosting for free until **August 1, 2025**. We're currently measuring usage and infrastructure costs. After that, we will introduce a pricing model based on:
>
> - The **total size of your hosted content**
> - The **amount of data delivered (traffic)**
>
> Standard usage volumes will be **included in existing Balancy plans**, and transparent pricing tiers will be published well before billing begins.

If you're already using Unity Addressables, integrating with Balancy takes just a few minutes — and gives you powerful tools to manage and deliver content at scale.

## Installation

### Prerequisites

First, install the latest Unity Addressables package from the Package Manager:

1. Open **Window ► Package Manager**
2. Search for "Addressables"
3. Click **Install**

### Install Balancy Addressables Plugin

1. In Unity, open **Tools ► Balancy ► Updates**
2. Install the **Addressables + CDN Hosting** plugin

### Add Assembly Reference

In the **asmdef** file of your game, add a reference to the **Balancy.Addressables** assembly. This is necessary for Addressables to work correctly with Balancy.

Example `YourGame.asmdef`:
```json
{
    "name": "YourGame",
    "references": [
        "Balancy",
        "Balancy.Addressables"
    ]
}
```

## Setup & Synchronization

### Step 1: Open Addressables Window

1. In Unity, open **Tools ► Balancy ► Config**
2. Click **Sync Addressables** button

### Step 2: Configure Settings

Follow these steps in the Addressables window:

1. **Create Balancy Profile**
   - Click the button to create a Balancy-specific Addressables profile
   - This configures your groups to use Balancy's CDN URLs

2. **Provide Private Key**
   - Enter your game's **Private Key** (found in the Balancy dashboard)
   - This is used for secure S2S API communication during upload

3. **Activate Addressables Groups**
   - Check the groups you want Balancy to host
   - Only selected groups will be uploaded to the CDN

Your setup should look like this:

![Addressables Setup](../img/addressables/addressables_setup.png)

### Step 3: Build & Upload

Click **Start Build** to begin the process:

1. **Build Phase**
   - Balancy generates Addressable bundles
   - Local copy stored in `Library/BalancyData` folder

2. **Upload Phase**
   - Bundles uploaded to Balancy's global CDN
   - Metadata JSON generated with asset URLs
   - Upload confirmation displayed

3. **Deployment**
   - Go to Balancy dashboard and refresh
   - Open **Assets/Addressables** section
   - You'll see previews of your uploaded assets
   - Click **Deploy** to make bundles publicly available

## Usage

### Loading Assets from Balancy Documents

If you have a document with an Asset parameter:

```csharp
// Load any asset type
document.MyAsset.LoadAsset(asset =>
{
    // Use the loaded asset
});

// Load sprite specifically
document.MyAsset.LoadSprite(sprite =>
{
    // Apply sprite to Image component
    image.sprite = sprite;
});
```

### Loading Assets by Name

You can also load assets directly by their addressable name:

```csharp
// Load any object type
Balancy.AssetsRuntime.GetObject("MyAssetName", asset =>
{
    // Use the loaded asset
});

// Load sprite specifically
Balancy.AssetsRuntime.GetSprite("MySpriteName", sprite =>
{
    // Apply sprite to Image component
    image.sprite = sprite;
});
```

## Testing

### Local Testing (Default)

By default, Unity uses local addressables for testing in the Editor. No additional setup needed.

### Testing with CDN (Production Mode)

To test the production version using Balancy's CDN:

1. Open **Window ► Asset Management ► Addressables ► Groups**
2. Select **Play Mode Script ► Use Existing Build**
3. Make sure you've built addressables using Balancy tools first

**Cross-Platform Testing:**

If you're testing addressables for a different platform than your Editor (e.g., iOS/Android on macOS):

```csharp
// During Balancy initialization
var config = new AppConfig();
config.SetDevicePlatform(Balancy.Constants.DevicePlatform.Android);
// Or: DevicePlatform.IPhonePlayer, etc.
Balancy.Main.Init(config);
```

This ensures the correct platform-specific bundles are loaded from the CDN.

## How It Works

1. **Build Time:**
   - Unity generates addressable bundles for each platform
   - Balancy uploads bundles to CDN with unique hash-based URLs
   - Asset catalog is generated and uploaded to Balancy servers

2. **Runtime:**
   - Balancy SDK downloads asset catalog on game start
   - Assets are loaded on-demand via Unity Addressables API
   - CDN delivers bundles based on player's platform
   - Automatic fallback from Sprite to Texture2D conversion if needed

3. **Updates:**
   - Rebuild and upload new bundles anytime
   - Deploy changes in Balancy dashboard
   - Players receive updates automatically (no app update required)

## Troubleshooting

### "Asset bundles built with build target X may not be compatible with running in the Editor"

**Solution:** This is a warning when testing platform-specific bundles in Editor. Either:
- Build bundles for your Editor platform for testing
- Or ignore the warning (bundles will still work in builds)

### "No Asset found for Key=X with Type=Sprite. Key exists as Type=Texture2D"

**Solution:** The module automatically handles this by:
1. Attempting to load as requested type (Sprite)
2. Falling back to Texture2D and converting to Sprite
3. This happens transparently - no code changes needed

### Double Slashes in CDN URLs

**Solution:** The module automatically normalizes URLs:
- Removes `BALANCY_URL` placeholder
- Strips extra slashes
- Ensures clean CDN URLs

## Support

For more information and support:
- 📖 [Full Documentation](https://docs.balancy.dev)
- 💬 [Discord Community](https://discord.gg/balancy)
- 📧 Email: support@balancy.dev

---

**Version:** 1.0.0
**Last Updated:** December 2024
**Requires:** Unity 2020.3+, Unity Addressables 1.19+, Balancy SDK 2.0+
