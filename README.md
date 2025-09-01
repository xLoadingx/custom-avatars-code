# **Custom Avatars - Installation & Rig Creation Guide**

## 1. Requirements
Before you install, make sure you have:
- RUMBLE (Duh)
- MelonLoader (v0.6.x or v0.7.0 for Il2Cpp)
- [RumbleModdingAPI](https://thunderstore.io/c/rumble/p/UlvakSkillz/RumbleModdingAPI) by UlvakSkillz

Once you got that setup, you can continue on to Step 2.

## 2. Installing the Mod

1. Go To your RUMBLE Folder
    - Steam: Right-click RUMBLE -> Properties -> Installed Files -> Browse
2. If there's no `Mods` folder, run the game once or reinstall MelonLoader.
3. Drag all the folders from the `.zip` into the RUMBLE folder.
4. Run the game. If you see `"[CustomAvatars] Mod Initialized"` in the console, you're good. Close the game after checking.

The reload keybind uses key names for the key, not the literal key itself.
For example, ";" would be "Semicolon", and "\\" would be Blackslash.

# How to Make a Custom Rig
**(WARNING: This is technical. If computers scare you, proceed with caution)**
**(WARNING 2.0: This mod only works with HUMANOID rigs. As in, it will not work with anything not human shaped)**

If you're better following visual tutorials, you can follow this great video by SaveForth! You can stop after adding the Interaction Profiles, then continue onto the 'Import & Modify your Rig' section.  
[Rumble Modding - Asset Bundle Tutorial](https://youtu.be/CImyvhpIAQ8)  

## Prepare the Rig in Blender
- One **Armature** + one **Mesh** only.
- Delete anything else not wanted in the scene.
- Export as `.FBX` named exactly `Rig` (case-sensitive).

- **IMPORTANT**:  
    To make your rig's proportions work with the one in game, you need to line it up with the in-game rig's bones. To do this, simply follow the steps below:
    1. Download the [in-game rig](https://github.com/xLoadingx/custom-avatars-code/raw/refs/heads/main/TutorialAssets/RumbleArmature.fbx) and import it into your Blender scene.
    2. Line it up with your rig as best you can. It doesn't have to be pixel-perfect. If your rig's proportions are *way* off, it might not be compatible. Make sure to scale your rig, rather than the rumble one.
    3. If it lines up well, select your Armature -> Edit Mode -> Move/Scale bones to roughly match the in-game Armature.
    4. Once done, delete the RUMBLE armature.

**If you're better at following text, however, you can follow the tutorial below**  

### Step 1 - Install Unity
- Use Unity 2022.3.24f1.
- In Unity Hub -> Installs -> Install Editor -> Archive -> Download Archive.
- Find 2022.3.24f1 (switch to "All Versions" if you can't see it) and install.
- Make a **Universal 3D** project.

### Step 2 - Install CustomAvatar's Unity SDK
1. Download the package from [GitHub](https://github.com/xLoadingx/custom-avatars-code/blob/main/TutorialAssets/CustomAvatars.unitypackage)
2. Open it, and a new window should pop up in unity. Click Import.

### Step 3 - Project Settings
- Edit -> Project Settings -> -> XR Plug-in Management -> Install XR Plugin Management.
- Enable OpenXR (do NOT enable Oculus). Give it a minute.
- When prompted, restart the editor.
- In OpenXR -> Enabled Interaction Profiles -> add:
    - Valve Index Controller Profile
    - Oculus Touch Controller Profile

# Step 4 Import & Modify Your Rig
1. Drag your `.FBX` into Unity's **Assets**, then drag it into the scene. Once in the scene, drag it back into the `Assets` folder and click `Prefab Variant` if prompted.
2. The root object name must be `Rig` (Case-Sensitive).
3. In the **Rig** tab of your FBX, set Animation Type -> Humanoid
4. Click 'Configure...' and check for any issues.
5. Set any bones that Unity didn't automatically set on the right hand side. Don't assign any bones you don't want to make follow the RUMBLE rig.

If your rig cannot be set as **Humanoid** (for example, if it has fewer than 15 bones), switch it to **Generic** instead.
In this case, each bone you want to follow in-game needs to be renamed to match the bone used by RUMBLE's rig.
You can find the full list of valid bone names [here](https://github.com/xLoadingx/custom-avatars-code/blob/main/TutorialAssets/BoneList.md)

I won't be covering fixess for specific FBX/rigging errors here, but you can usually find a solution online, or ask in the Discord!

6. In your assets folder, go to Create -> CustomAvatars -> AvatarDescriptor. Select the new descriptor (you can name it anything), and assign your newly created Prefab Variant to the top 'Avatar Prefab' slot.

# What is an Avatar Descriptor?
- An avatar descriptor basically helps the mod know how to affect your avatar. Here is a more convoluted explanation:
    - Swap Original Mesh: A toggle to check if you want to make your avatar get rid of your in game one. This can be turned off if you simply wanted to add things to your avatar (with a Generic rig).
    
    - Player Shader Slots: This is a toggle for all your material slots to use the player shader. For example, if you were to select your 'Body' material as a player shader, it would replace it in game. This requires the mesh to have a RED vertex color in order to hide the head. The material you select has to have a "BaseMap" texture option in the shader, which comes default with URP/Lit, URP/Unlit, and others.

    - Body Material: Sadly, Rock Cam only currently supports one material for the player. The material you select here will be the one that shows in Rock Cam. **MAKE SURE** the shader is uses has a "IsLocal" bool that toggles the head on and off. You can either use the sample shaders provided in the 'CustomAvatarsSDK' folder, or make your own (Shader Graph or HLSL works fine).

    - BlendShapes (if your avatar has them);
        - Blink Type: A simple option, which depends if your eyes have seperate blendshapes for blinking, or the same one. You can select them accordingly.
            - Single: Select your single blendshape for blinking
            - Left & Right: Select your blinking blendshapes for the corresponding eye.
        
        - Jaw Open: Currently it only supports blendshapes for speaking. This is the blendshape that moves when you talk. You can adjust the sensitivity with the 'Volume Multiplier' option, if the mouth moves too little or too much.

        - Blink Settings
            - Auto Blink: Toggles blinking
            - Blink Interval: The random value that waits between each blink. X is minimum, Y is maximum.
            - Blink speed: How long each blink takes (in seconds).

        - Default BlendShapes
            - This is a list of blendshapes that it sets defaultly when you load in. If you have any blendshapes you want to toggle, set them here.
            - You can click "Add BlendShape" and set the values accordingly. If you want to take them from blender, make sure to multiply them by 100, as Unity uses 0-100, rather than Blender's 0-1.

**Once you're done with all of these settings, you can hit the 'Export Config' button to choose the location you want to save. The location does not matter.**

### Step 5 - Optional Cool Stuff
Anything really that you know how to do. However, the one limitation with this mod is that AnimatorControllers **do not** work. 

### Step 6 - Build Asset Bundle
1. Custom Avatars (top bar) -> RUMBLE Avatar Builder -> Drag in your Avatar's Avatar Descriptor.
2. Set your output path (preferablly your CustomAvatars folder: 'RUMBLE/UserData/CustomAvatars'), and then hit 'Build Bundle'.

If you did not set it to your CustomAvatars folder, you'll have to drag it in manually.

### Step 7 - Test It
- Launch RUMBLE. Your rig should load on your local player.
- Upload via ModUI -> Upload Avatar to allow other players to see it. No need to click the Save button.

## Common Issues (a.k.a. Why Is My Avatar Cursed?)
**Hands rotated 90°**
- In Blender Pose Mode -> select hand bone -> rotate ±90° on Y-axis -> Ctrl+A -> Apply Rest Pose -> export -> rebuild.
- You can do this for any other bone that is rotated incorrectly on the corresponding axis.

**Fingers turn into an abomination**
- Still no fix. Likely bone proportions not matching RUMBLE's rig exactly. Sorry.
- If it does line up well, however, it should be mostly fine.

**I can see my own head in game**
- Make sure your shader(s) have an "IsLocal" property on them. This is what's toggled on and off depending on the player (ex. head shows for others, but not yourself)

**All of my materials are pink!**
- Go to the 'Create' menu and click the little down arrow at the bottom. Go until you see the 'Rendering' tab and create a new Render Asset for Universal.
- Then, go to Edit -> Project Settings -> Graphics, and then drag this asset into your "Universal Render Pipeline Asset"

**The whole player's shading changes when the blendshapes are changed**
- Set the 'Blendshape Normals' in the FBX 'Rig' tab to 'Import' instead of 'Calculate'

##  

Need help?
Join the [Rumble Modding Discord](https://discord.gg/BeWpUXqjtH) and ping `error_real_sir` or `ERROR - ButtonBender`. I'll try to help.
