Thank You for using Customizable Levers And Buttons - Pack :-)
Please send any questions to bigblit@protonmail.com

The Asset is in active development. More cool things will come!


To start:
1. If you are using HDRP please install LeversAndButtons_HDRP.unityPackage or LeversAndButtons_HDRP_Preview.unityPackage (for preview versions of HDRP) first.
2. Add new layer that will be used for interactve objects let's call it "interactive layer" (or use an old one).
3. Add PhysicsRaycaster component to your scene camera.
4. In the PhysicsRaycaster component set Event Mask to interactive layer.
5. Drag one of the prefabs from the project view from one of BigBlit/ActivePack/LeversAndButtons/Prefabs subfolders into the scene or hierarchy view and set its layer to interactive layer.
6a) Legacy input system only: make sure that "Event System" and "Standalone Input Module" is in the scene.  
6b) The New Input System only: Add "Event System", "Input System UI Input Module" and "Player Input" components. 
Make sure that "UI Input Module" field in the "Player Input" is set to your newly added "Input System UI Input Module".
Create your own action map or use the one with the package.

Thats it.

More info:
In addition to clean prefabs that contain mesh, material and collider three types of buttons were also prefabed: 
Press-click buttons, simple Toggles, and Switches.
Additionaly you may customize behaviour of each buttton/lever by changing its animation clip and preferences of its components and adding/removing components provided with this asset.

The components are:

Buttons behaviours:
PressButton - Pressable Button. Implements press behaviour and events.
ClickButton - Clickable Button. Implements click and long click behaviour and events.
ToggleButton - Toggleable Button. Implements toggle on/toggle of behaviour and events.
ButtonSwitch - Add swtich behaviour to group of ToggleButtons.
Lever - Implements Lever behaviours and events.

UnityEvents Triggers:
PressButtonEventTrigger - Converts native IPressable interface events to Unity Events.
ClickableEventTrigger -  Converts native IClickable (ex. click button) events to Unity Events.
ToggleableEventTrigger - Converts native IToggleable interface events to Unity Events.
DraggableEventTrigger - Converts native IDraggable interface events to Unity Events.

Input:
KeyboardPressController - Keyboard input controller for all pressable objects.
PointerPressController - Pointer controller for all pressable objects.
PointerDragController - Pointer controller for drag events
CircularPointerDragController - Pointer controller for circular drag gesture.


NOTICE: The PointerPressController  controller is based on Unity Event System. 
For it to work please make sure that:
 - You have unity Event System configured.
 - Camera has PhysicsRaycaster component added.
 - PhysicsRaycaster EventMask layer and ActiveObjects layers are properly set.

Misc:
ColorChanger - A color changer that uses PropertyBlock to change material colors efficiently.
EmissionController - Emission controller that react on ActiveObject events and uses PropertyBlock to change material emission efficiently. Used to add lighting to the buttons decals.
ValueAnimator - Animates GameObject that implements IValueable interface by using Playables and AnimationClips.
