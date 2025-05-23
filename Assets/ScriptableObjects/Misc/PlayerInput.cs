//------------------------------------------------------------------------------
// <auto-generated>
//     This code was auto-generated by com.unity.inputsystem:InputActionCodeGenerator
//     version 1.7.0
//     from Assets/ScriptableObjects/Misc/PlayerInput.inputactions
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

public partial class @PlayerInput: IInputActionCollection2, IDisposable
{
    public InputActionAsset asset { get; }
    public @PlayerInput()
    {
        asset = InputActionAsset.FromJson(@"{
    ""name"": ""PlayerInput"",
    ""maps"": [
        {
            ""name"": ""Player"",
            ""id"": ""c1cc24b7-9f58-44d8-987c-e9da372bcbfe"",
            ""actions"": [
                {
                    ""name"": ""Shoot"",
                    ""type"": ""Button"",
                    ""id"": ""dcb95e20-3830-434d-b7ca-34e333345ac1"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": ""Press(behavior=2)"",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Movement"",
                    ""type"": ""Button"",
                    ""id"": ""cef9e4bb-d2a8-4d67-849d-00b2f48d7f1b"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Jump"",
                    ""type"": ""Button"",
                    ""id"": ""e0bb7f70-8d76-460b-bc77-07867cb2a692"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Spell1"",
                    ""type"": ""Button"",
                    ""id"": ""f726c55c-892d-4ad7-9fea-26e3dbbae705"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Spell2"",
                    ""type"": ""Button"",
                    ""id"": ""24ca7431-5d43-44d1-a1c8-303a13e5dbb0"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Spell3"",
                    ""type"": ""Button"",
                    ""id"": ""cac6beff-9c73-433f-9f19-d05c2dddfc8d"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Interact"",
                    ""type"": ""Button"",
                    ""id"": ""a2c000bc-78ec-4522-a094-407c0abbb694"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""SwapWeapon"",
                    ""type"": ""Button"",
                    ""id"": ""b6c46ada-ec3a-4671-a68a-1526155f9b1a"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Map"",
                    ""type"": ""Button"",
                    ""id"": ""9c3eb5cb-92f5-438a-aef9-9836fa0977f7"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""LockMap"",
                    ""type"": ""Button"",
                    ""id"": ""2826b482-6e7e-4af9-8098-8523f4ec7c20"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Portal"",
                    ""type"": ""Button"",
                    ""id"": ""254c25d1-8b41-4097-a449-db452b2cb230"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Potion1"",
                    ""type"": ""Button"",
                    ""id"": ""b7f93244-0833-476d-8521-3989e0c863dc"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": ""Tap(duration=0.75)"",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Potion2"",
                    ""type"": ""Button"",
                    ""id"": ""ac876ba9-71f8-4c8c-be7f-d36e33789bf2"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": ""Tap(duration=0.75)"",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Potion3"",
                    ""type"": ""Button"",
                    ""id"": ""1979904b-988f-4da2-9758-d49594b262c7"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": ""Tap(duration=0.75)"",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Reload"",
                    ""type"": ""Button"",
                    ""id"": ""ef427e60-8644-489b-9fb4-cf11c5127c16"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Escape"",
                    ""type"": ""Button"",
                    ""id"": ""ddd1dc6d-22da-4ff3-9cd0-36712931e5bf"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Build"",
                    ""type"": ""Button"",
                    ""id"": ""606f5ef0-65a4-4a13-8427-7ad6370f21d9"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                },
                {
                    ""name"": ""Aim"",
                    ""type"": ""Button"",
                    ""id"": ""e60ac59d-43e0-4a7f-80ff-5f8c37c7c03d"",
                    ""expectedControlType"": ""Button"",
                    ""processors"": """",
                    ""interactions"": """",
                    ""initialStateCheck"": false
                }
            ],
            ""bindings"": [
                {
                    ""name"": ""2D Vector"",
                    ""id"": ""f57d33f6-321e-419e-ab48-ef1fe37cd199"",
                    ""path"": ""2DVector"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Movement"",
                    ""isComposite"": true,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": ""up"",
                    ""id"": ""8eb209d3-8732-4d25-b39c-e75a62c00494"",
                    ""path"": ""<Keyboard>/w"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Movement"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""down"",
                    ""id"": ""4f19a3df-f534-47d6-87b7-b0738ccd0a56"",
                    ""path"": ""<Keyboard>/s"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Movement"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""left"",
                    ""id"": ""91f9511a-0048-4bf1-8ef9-feab68db8936"",
                    ""path"": ""<Keyboard>/a"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Movement"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": ""right"",
                    ""id"": ""fa7b4a5f-7833-4286-b4fe-06e8a5d4935a"",
                    ""path"": ""<Keyboard>/d"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Movement"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": true
                },
                {
                    ""name"": """",
                    ""id"": ""37450773-79a5-4041-8792-1c5038cf5ccd"",
                    ""path"": ""<Gamepad>/leftStick"",
                    ""interactions"": """",
                    ""processors"": ""AxisDeadzone(min=0.02,max=0.925)"",
                    ""groups"": """",
                    ""action"": ""Movement"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""b7105fe5-1abe-4f6b-a46d-f69d985c2cbc"",
                    ""path"": ""<Keyboard>/space"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Jump"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""93abc183-5124-41bb-a413-cdfecd1635e6"",
                    ""path"": ""<Gamepad>/leftStickPress"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Jump"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""382aa982-8542-411c-9760-dc0a57d00259"",
                    ""path"": ""<Keyboard>/z"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Spell1"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""476008f0-3252-4b9d-a766-4dd6682dd875"",
                    ""path"": ""<Gamepad>/leftTrigger"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Spell1"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""586ffd8a-86fa-4edf-b65e-7dd11d859a6f"",
                    ""path"": ""<Keyboard>/x"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Spell2"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""19b734d8-6f4a-4084-a864-e1f78c5f128a"",
                    ""path"": ""<Gamepad>/leftShoulder"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Spell2"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""2dbaf907-f642-4c79-88be-d60ddec155e0"",
                    ""path"": ""<Keyboard>/c"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Spell3"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""599465be-9f4c-4302-ac95-9c17556ab1ff"",
                    ""path"": ""<Mouse>/rightButton"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Shoot"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""d6728e23-0ab1-4516-962c-e030c22636ea"",
                    ""path"": ""<Gamepad>/rightShoulder"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Shoot"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""19461484-d4af-4a1b-a8c8-09b11495f955"",
                    ""path"": ""<Mouse>/leftButton"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Interact"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""430395a0-34bf-4572-b2af-9544364264c1"",
                    ""path"": ""<Gamepad>/buttonSouth"",
                    ""interactions"": """",
                    ""processors"": ""AxisDeadzone(min=0.7)"",
                    ""groups"": """",
                    ""action"": ""Interact"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""6dea3aca-09d9-45c7-9566-c46817c3f979"",
                    ""path"": ""<Keyboard>/tab"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""SwapWeapon"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""b196f74b-cb9e-44a2-a2e6-f28988b9b543"",
                    ""path"": ""<Gamepad>/buttonNorth"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""SwapWeapon"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""187abd34-869c-427a-be19-94e10395fb4d"",
                    ""path"": ""<Keyboard>/m"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Map"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""5bc2cb1a-da0a-46c2-b2bc-a59295ba765c"",
                    ""path"": ""<Gamepad>/rightStickPress"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Map"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""2ff17942-f6a6-4d26-9bce-7aae809efe99"",
                    ""path"": ""<Keyboard>/l"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""LockMap"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""5887d4cf-7e08-4929-bf7e-52b1f17a7e9d"",
                    ""path"": ""<Gamepad>/select"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""LockMap"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""62ac1225-c994-4272-964a-f78a80ccaab8"",
                    ""path"": ""<Keyboard>/v"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Portal"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""e27c5fb3-df58-4a10-a61f-ce402bb5958c"",
                    ""path"": ""<Gamepad>/buttonEast"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Portal"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""3104dc57-c2f7-478b-976f-ef1b4e344150"",
                    ""path"": ""<Keyboard>/1"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Potion1"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""7d88d90c-370d-4617-bb2d-4d81604fd5e4"",
                    ""path"": ""<Gamepad>/dpad/left"",
                    ""interactions"": """",
                    ""processors"": ""AxisDeadzone(min=0.5)"",
                    ""groups"": """",
                    ""action"": ""Potion1"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""22b764c4-3eef-4869-83c7-9e081655886a"",
                    ""path"": ""<Keyboard>/2"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Potion2"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""c238a1d6-e3cf-4e34-8b2d-b4132a1b2b1e"",
                    ""path"": ""<DualSenseGamepadHID>/dpad/up"",
                    ""interactions"": """",
                    ""processors"": ""AxisDeadzone(min=0.5)"",
                    ""groups"": """",
                    ""action"": ""Potion2"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""29e5b71d-ac12-49e6-8926-c57eaec18707"",
                    ""path"": ""<Keyboard>/3"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Potion3"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""b3ebe927-340a-4867-bdc3-459fd1424a9d"",
                    ""path"": ""<Gamepad>/dpad/right"",
                    ""interactions"": """",
                    ""processors"": ""AxisDeadzone(min=0.5)"",
                    ""groups"": """",
                    ""action"": ""Potion3"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""de41309c-3e16-445b-9a7c-4271daa4b6e6"",
                    ""path"": ""<Keyboard>/r"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Reload"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""763c950a-a233-40b4-9c87-bda3d3d87048"",
                    ""path"": ""<Gamepad>/buttonWest"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Reload"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""e408c007-34aa-4029-8a08-06b1d5a24348"",
                    ""path"": ""<Gamepad>/rightTrigger"",
                    ""interactions"": ""Press(behavior=1)"",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Spell3"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""e4a1b8b3-8fd2-4ebe-a9ed-37b9f69642f8"",
                    ""path"": ""<Keyboard>/escape"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Escape"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""e1d3ab3a-12cc-405f-8dd2-903352bffd4d"",
                    ""path"": ""<Gamepad>/start"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Escape"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""1adcc6f0-9255-443e-ae50-4090ad8401ce"",
                    ""path"": ""<Keyboard>/b"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Build"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""73fc492d-5ca2-42c2-8be0-56575e5ede4d"",
                    ""path"": ""<Gamepad>/dpad/down"",
                    ""interactions"": """",
                    ""processors"": ""AxisDeadzone(min=0.7)"",
                    ""groups"": """",
                    ""action"": ""Build"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                },
                {
                    ""name"": """",
                    ""id"": ""442a4397-7365-4e17-aa3c-9bccf86285fe"",
                    ""path"": ""<Gamepad>/rightStick"",
                    ""interactions"": """",
                    ""processors"": """",
                    ""groups"": """",
                    ""action"": ""Aim"",
                    ""isComposite"": false,
                    ""isPartOfComposite"": false
                }
            ]
        }
    ],
    ""controlSchemes"": []
}");
        // Player
        m_Player = asset.FindActionMap("Player", throwIfNotFound: true);
        m_Player_Shoot = m_Player.FindAction("Shoot", throwIfNotFound: true);
        m_Player_Movement = m_Player.FindAction("Movement", throwIfNotFound: true);
        m_Player_Jump = m_Player.FindAction("Jump", throwIfNotFound: true);
        m_Player_Spell1 = m_Player.FindAction("Spell1", throwIfNotFound: true);
        m_Player_Spell2 = m_Player.FindAction("Spell2", throwIfNotFound: true);
        m_Player_Spell3 = m_Player.FindAction("Spell3", throwIfNotFound: true);
        m_Player_Interact = m_Player.FindAction("Interact", throwIfNotFound: true);
        m_Player_SwapWeapon = m_Player.FindAction("SwapWeapon", throwIfNotFound: true);
        m_Player_Map = m_Player.FindAction("Map", throwIfNotFound: true);
        m_Player_LockMap = m_Player.FindAction("LockMap", throwIfNotFound: true);
        m_Player_Portal = m_Player.FindAction("Portal", throwIfNotFound: true);
        m_Player_Potion1 = m_Player.FindAction("Potion1", throwIfNotFound: true);
        m_Player_Potion2 = m_Player.FindAction("Potion2", throwIfNotFound: true);
        m_Player_Potion3 = m_Player.FindAction("Potion3", throwIfNotFound: true);
        m_Player_Reload = m_Player.FindAction("Reload", throwIfNotFound: true);
        m_Player_Escape = m_Player.FindAction("Escape", throwIfNotFound: true);
        m_Player_Build = m_Player.FindAction("Build", throwIfNotFound: true);
        m_Player_Aim = m_Player.FindAction("Aim", throwIfNotFound: true);
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(asset);
    }

    public InputBinding? bindingMask
    {
        get => asset.bindingMask;
        set => asset.bindingMask = value;
    }

    public ReadOnlyArray<InputDevice>? devices
    {
        get => asset.devices;
        set => asset.devices = value;
    }

    public ReadOnlyArray<InputControlScheme> controlSchemes => asset.controlSchemes;

    public bool Contains(InputAction action)
    {
        return asset.Contains(action);
    }

    public IEnumerator<InputAction> GetEnumerator()
    {
        return asset.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void Enable()
    {
        asset.Enable();
    }

    public void Disable()
    {
        asset.Disable();
    }

    public IEnumerable<InputBinding> bindings => asset.bindings;

    public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false)
    {
        return asset.FindAction(actionNameOrId, throwIfNotFound);
    }

    public int FindBinding(InputBinding bindingMask, out InputAction action)
    {
        return asset.FindBinding(bindingMask, out action);
    }

    // Player
    private readonly InputActionMap m_Player;
    private List<IPlayerActions> m_PlayerActionsCallbackInterfaces = new List<IPlayerActions>();
    private readonly InputAction m_Player_Shoot;
    private readonly InputAction m_Player_Movement;
    private readonly InputAction m_Player_Jump;
    private readonly InputAction m_Player_Spell1;
    private readonly InputAction m_Player_Spell2;
    private readonly InputAction m_Player_Spell3;
    private readonly InputAction m_Player_Interact;
    private readonly InputAction m_Player_SwapWeapon;
    private readonly InputAction m_Player_Map;
    private readonly InputAction m_Player_LockMap;
    private readonly InputAction m_Player_Portal;
    private readonly InputAction m_Player_Potion1;
    private readonly InputAction m_Player_Potion2;
    private readonly InputAction m_Player_Potion3;
    private readonly InputAction m_Player_Reload;
    private readonly InputAction m_Player_Escape;
    private readonly InputAction m_Player_Build;
    private readonly InputAction m_Player_Aim;
    public struct PlayerActions
    {
        private @PlayerInput m_Wrapper;
        public PlayerActions(@PlayerInput wrapper) { m_Wrapper = wrapper; }
        public InputAction @Shoot => m_Wrapper.m_Player_Shoot;
        public InputAction @Movement => m_Wrapper.m_Player_Movement;
        public InputAction @Jump => m_Wrapper.m_Player_Jump;
        public InputAction @Spell1 => m_Wrapper.m_Player_Spell1;
        public InputAction @Spell2 => m_Wrapper.m_Player_Spell2;
        public InputAction @Spell3 => m_Wrapper.m_Player_Spell3;
        public InputAction @Interact => m_Wrapper.m_Player_Interact;
        public InputAction @SwapWeapon => m_Wrapper.m_Player_SwapWeapon;
        public InputAction @Map => m_Wrapper.m_Player_Map;
        public InputAction @LockMap => m_Wrapper.m_Player_LockMap;
        public InputAction @Portal => m_Wrapper.m_Player_Portal;
        public InputAction @Potion1 => m_Wrapper.m_Player_Potion1;
        public InputAction @Potion2 => m_Wrapper.m_Player_Potion2;
        public InputAction @Potion3 => m_Wrapper.m_Player_Potion3;
        public InputAction @Reload => m_Wrapper.m_Player_Reload;
        public InputAction @Escape => m_Wrapper.m_Player_Escape;
        public InputAction @Build => m_Wrapper.m_Player_Build;
        public InputAction @Aim => m_Wrapper.m_Player_Aim;
        public InputActionMap Get() { return m_Wrapper.m_Player; }
        public void Enable() { Get().Enable(); }
        public void Disable() { Get().Disable(); }
        public bool enabled => Get().enabled;
        public static implicit operator InputActionMap(PlayerActions set) { return set.Get(); }
        public void AddCallbacks(IPlayerActions instance)
        {
            if (instance == null || m_Wrapper.m_PlayerActionsCallbackInterfaces.Contains(instance)) return;
            m_Wrapper.m_PlayerActionsCallbackInterfaces.Add(instance);
            @Shoot.started += instance.OnShoot;
            @Shoot.performed += instance.OnShoot;
            @Shoot.canceled += instance.OnShoot;
            @Movement.started += instance.OnMovement;
            @Movement.performed += instance.OnMovement;
            @Movement.canceled += instance.OnMovement;
            @Jump.started += instance.OnJump;
            @Jump.performed += instance.OnJump;
            @Jump.canceled += instance.OnJump;
            @Spell1.started += instance.OnSpell1;
            @Spell1.performed += instance.OnSpell1;
            @Spell1.canceled += instance.OnSpell1;
            @Spell2.started += instance.OnSpell2;
            @Spell2.performed += instance.OnSpell2;
            @Spell2.canceled += instance.OnSpell2;
            @Spell3.started += instance.OnSpell3;
            @Spell3.performed += instance.OnSpell3;
            @Spell3.canceled += instance.OnSpell3;
            @Interact.started += instance.OnInteract;
            @Interact.performed += instance.OnInteract;
            @Interact.canceled += instance.OnInteract;
            @SwapWeapon.started += instance.OnSwapWeapon;
            @SwapWeapon.performed += instance.OnSwapWeapon;
            @SwapWeapon.canceled += instance.OnSwapWeapon;
            @Map.started += instance.OnMap;
            @Map.performed += instance.OnMap;
            @Map.canceled += instance.OnMap;
            @LockMap.started += instance.OnLockMap;
            @LockMap.performed += instance.OnLockMap;
            @LockMap.canceled += instance.OnLockMap;
            @Portal.started += instance.OnPortal;
            @Portal.performed += instance.OnPortal;
            @Portal.canceled += instance.OnPortal;
            @Potion1.started += instance.OnPotion1;
            @Potion1.performed += instance.OnPotion1;
            @Potion1.canceled += instance.OnPotion1;
            @Potion2.started += instance.OnPotion2;
            @Potion2.performed += instance.OnPotion2;
            @Potion2.canceled += instance.OnPotion2;
            @Potion3.started += instance.OnPotion3;
            @Potion3.performed += instance.OnPotion3;
            @Potion3.canceled += instance.OnPotion3;
            @Reload.started += instance.OnReload;
            @Reload.performed += instance.OnReload;
            @Reload.canceled += instance.OnReload;
            @Escape.started += instance.OnEscape;
            @Escape.performed += instance.OnEscape;
            @Escape.canceled += instance.OnEscape;
            @Build.started += instance.OnBuild;
            @Build.performed += instance.OnBuild;
            @Build.canceled += instance.OnBuild;
            @Aim.started += instance.OnAim;
            @Aim.performed += instance.OnAim;
            @Aim.canceled += instance.OnAim;
        }

        private void UnregisterCallbacks(IPlayerActions instance)
        {
            @Shoot.started -= instance.OnShoot;
            @Shoot.performed -= instance.OnShoot;
            @Shoot.canceled -= instance.OnShoot;
            @Movement.started -= instance.OnMovement;
            @Movement.performed -= instance.OnMovement;
            @Movement.canceled -= instance.OnMovement;
            @Jump.started -= instance.OnJump;
            @Jump.performed -= instance.OnJump;
            @Jump.canceled -= instance.OnJump;
            @Spell1.started -= instance.OnSpell1;
            @Spell1.performed -= instance.OnSpell1;
            @Spell1.canceled -= instance.OnSpell1;
            @Spell2.started -= instance.OnSpell2;
            @Spell2.performed -= instance.OnSpell2;
            @Spell2.canceled -= instance.OnSpell2;
            @Spell3.started -= instance.OnSpell3;
            @Spell3.performed -= instance.OnSpell3;
            @Spell3.canceled -= instance.OnSpell3;
            @Interact.started -= instance.OnInteract;
            @Interact.performed -= instance.OnInteract;
            @Interact.canceled -= instance.OnInteract;
            @SwapWeapon.started -= instance.OnSwapWeapon;
            @SwapWeapon.performed -= instance.OnSwapWeapon;
            @SwapWeapon.canceled -= instance.OnSwapWeapon;
            @Map.started -= instance.OnMap;
            @Map.performed -= instance.OnMap;
            @Map.canceled -= instance.OnMap;
            @LockMap.started -= instance.OnLockMap;
            @LockMap.performed -= instance.OnLockMap;
            @LockMap.canceled -= instance.OnLockMap;
            @Portal.started -= instance.OnPortal;
            @Portal.performed -= instance.OnPortal;
            @Portal.canceled -= instance.OnPortal;
            @Potion1.started -= instance.OnPotion1;
            @Potion1.performed -= instance.OnPotion1;
            @Potion1.canceled -= instance.OnPotion1;
            @Potion2.started -= instance.OnPotion2;
            @Potion2.performed -= instance.OnPotion2;
            @Potion2.canceled -= instance.OnPotion2;
            @Potion3.started -= instance.OnPotion3;
            @Potion3.performed -= instance.OnPotion3;
            @Potion3.canceled -= instance.OnPotion3;
            @Reload.started -= instance.OnReload;
            @Reload.performed -= instance.OnReload;
            @Reload.canceled -= instance.OnReload;
            @Escape.started -= instance.OnEscape;
            @Escape.performed -= instance.OnEscape;
            @Escape.canceled -= instance.OnEscape;
            @Build.started -= instance.OnBuild;
            @Build.performed -= instance.OnBuild;
            @Build.canceled -= instance.OnBuild;
            @Aim.started -= instance.OnAim;
            @Aim.performed -= instance.OnAim;
            @Aim.canceled -= instance.OnAim;
        }

        public void RemoveCallbacks(IPlayerActions instance)
        {
            if (m_Wrapper.m_PlayerActionsCallbackInterfaces.Remove(instance))
                UnregisterCallbacks(instance);
        }

        public void SetCallbacks(IPlayerActions instance)
        {
            foreach (var item in m_Wrapper.m_PlayerActionsCallbackInterfaces)
                UnregisterCallbacks(item);
            m_Wrapper.m_PlayerActionsCallbackInterfaces.Clear();
            AddCallbacks(instance);
        }
    }
    public PlayerActions @Player => new PlayerActions(this);
    public interface IPlayerActions
    {
        void OnShoot(InputAction.CallbackContext context);
        void OnMovement(InputAction.CallbackContext context);
        void OnJump(InputAction.CallbackContext context);
        void OnSpell1(InputAction.CallbackContext context);
        void OnSpell2(InputAction.CallbackContext context);
        void OnSpell3(InputAction.CallbackContext context);
        void OnInteract(InputAction.CallbackContext context);
        void OnSwapWeapon(InputAction.CallbackContext context);
        void OnMap(InputAction.CallbackContext context);
        void OnLockMap(InputAction.CallbackContext context);
        void OnPortal(InputAction.CallbackContext context);
        void OnPotion1(InputAction.CallbackContext context);
        void OnPotion2(InputAction.CallbackContext context);
        void OnPotion3(InputAction.CallbackContext context);
        void OnReload(InputAction.CallbackContext context);
        void OnEscape(InputAction.CallbackContext context);
        void OnBuild(InputAction.CallbackContext context);
        void OnAim(InputAction.CallbackContext context);
    }
}
