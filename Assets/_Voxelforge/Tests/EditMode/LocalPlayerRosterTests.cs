using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Press-to-join and per-player device pairing (Roadmap 2.2, D-008), driven with virtual devices.</summary>
    public class LocalPlayerRosterTests : InputTestFixture
    {
        const string ActionsPath = "Assets/_Voxelforge/Input/VoxelforgeControls.inputactions";

        sealed class RecordingJoinRequester : ILocalJoinRequester
        {
            public readonly List<byte> Requests = new List<byte>();
            public void RequestJoin(byte localSlot) => Requests.Add(localSlot);
        }

        RecordingJoinRequester _requester;
        LocalPlayerRoster _roster;

        public override void Setup()
        {
            base.Setup();
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            Assert.IsNotNull(actions, $"Missing {ActionsPath}");
            _requester = new RecordingJoinRequester();
            _roster = new LocalPlayerRoster(actions, 4, _requester);
        }

        public override void TearDown()
        {
            _roster.Dispose();
            base.TearDown();
        }

        Gamepad JoinGamepad()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            Press(gamepad.startButton);
            Release(gamepad.startButton);
            return gamepad;
        }

        [Test]
        public void GamepadStart_JoinsSlotZero()
        {
            Gamepad gamepad = JoinGamepad();

            Assert.AreEqual(LocalSlotState.Joining, _roster.GetState(0));
            CollectionAssert.AreEqual(new byte[] { 0 }, _requester.Requests);
            Assert.IsTrue(_roster.GetInput(0).Owns(gamepad));
            Assert.AreEqual(LocalPlayerRoster.GamepadScheme, _roster.GetInput(0).ControlScheme);
        }

        [Test]
        public void Keyboard_JoinsTogetherWithMouse()
        {
            JoinGamepad();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();

            Press(keyboard.enterKey);

            LocalPlayerInput input = _roster.GetInput(1);
            Assert.AreEqual(LocalPlayerRoster.KeyboardMouseScheme, input.ControlScheme);
            Assert.IsTrue(input.Owns(keyboard));
            Assert.IsTrue(input.Owns(mouse));
        }

        [Test]
        public void SameDevice_NeverJoinsTwice()
        {
            Gamepad gamepad = JoinGamepad();
            Press(gamepad.buttonSouth);

            CollectionAssert.AreEqual(new byte[] { 0 }, _requester.Requests);
            Assert.AreEqual(LocalSlotState.Free, _roster.GetState(1));
        }

        [Test]
        public void EachPlayer_OnlyHearsTheirOwnGamepad()
        {
            JoinGamepad();
            Gamepad second = JoinGamepad();

            Set(second.leftStick, new UnityEngine.Vector2(0f, 1f));

            Assert.AreEqual(0f, _roster.GetInput(0).Read().Move.y, 1e-4f);
            Assert.Greater(_roster.GetInput(1).Read().Move.y, 0.9f);
        }

        [Test]
        public void UnpluggedGamepad_DisconnectsAndComesBack()
        {
            Gamepad gamepad = JoinGamepad();
            _roster.MarkSpawned(0);
            Set(gamepad.leftStick, new UnityEngine.Vector2(1f, 0f));

            InputSystem.RemoveDevice(gamepad);

            Assert.AreEqual(LocalSlotState.Disconnected, _roster.GetState(0));
            Assert.AreEqual(default(PlayerInputFrame).Move, _roster.GetInput(0).Read().Move);

            InputSystem.AddDevice(gamepad);

            Assert.AreEqual(LocalSlotState.Active, _roster.GetState(0));
            Assert.IsFalse(_roster.GetInput(0).IsDisconnected);
        }

        [Test]
        public void NewGamepad_TakesOverDisconnectedSlot()
        {
            Gamepad first = JoinGamepad();
            JoinGamepad();
            _roster.MarkSpawned(0);
            _roster.MarkSpawned(1);
            InputSystem.RemoveDevice(first);

            Gamepad replacement = JoinGamepad();

            Assert.AreEqual(LocalSlotState.Active, _roster.GetState(0));
            Assert.IsTrue(_roster.GetInput(0).Owns(replacement));
            Assert.AreEqual(2, _requester.Requests.Count, "Taking over an existing player must not spawn another one.");
        }

        [Test]
        public void SplitKeyboard_GivesTwoPlayersTheirOwnKeys()
        {
            _roster.AllowSplitKeyboard = true;
            var keyboard = InputSystem.AddDevice<Keyboard>();

            Press(keyboard.spaceKey);
            Release(keyboard.spaceKey);
            Press(keyboard.enterKey);
            Release(keyboard.enterKey);
            Press(keyboard.dKey);

            Assert.AreEqual(LocalPlayerRoster.KeyboardLeftScheme, _roster.GetInput(0).ControlScheme);
            Assert.AreEqual(LocalPlayerRoster.KeyboardRightScheme, _roster.GetInput(1).ControlScheme);
            Assert.Greater(_roster.GetInput(0).Read().Move.x, 0.9f);
            Assert.AreEqual(0f, _roster.GetInput(1).Read().Move.x, 1e-4f);
        }

        [Test]
        public void WithoutSplitKeyboard_KeyboardJoinsOnlyOnce()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();

            Press(keyboard.enterKey);
            Release(keyboard.enterKey);
            Press(keyboard.spaceKey);

            CollectionAssert.AreEqual(new byte[] { 0 }, _requester.Requests);
        }
    }
}
