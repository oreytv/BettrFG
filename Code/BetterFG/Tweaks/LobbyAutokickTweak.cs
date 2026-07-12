using System;
using System.Collections.Generic;
using Catapult.Protocol;
using FallGuys.Lobby.Protocol.Client.Lobbies;
using Il2CppInterop.Runtime;
using BetterFG.Services;
using BetterFG.UI.Windows;
using UniRx;
using UnityEngine;

namespace BetterFG.Tweaks
{
#pragma warning disable CS8981
    public class LobbyAutokickTweak : BfgTweak
    {
        private float _nextCheck;
        private readonly Dictionary<string, float> _lastKick = new Dictionary<string, float>();
        public static readonly string[] DefaultChecks = { "size", "<", ">", "\\u003" };

        private const string CountKey = "tweak.lobby_autokick.contains.count";

        public LobbyAutokickTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lobby_autokick";
        public override string TweakLabel => "Lobby Autokick Bad Names";
        public override bool DefaultEnabled => true;

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "CFG", Width = 30f, OnClick = OpenConfig }
        };

        public override void EnableTweak()
        {
            _nextCheck = 0f;
        }

        private void OpenConfig()
        {
            if (LobbyAutokickConfigWindow.Instance != null)
            {
                LobbyAutokickConfigWindow.Instance.Close();
                return;
            }

            var go = new GameObject("BetterFG_LobbyAutokickConfigWindow");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<LobbyAutokickConfigWindow>().Configure(this);
        }

        private void Update()
        {
            if (!IsEnabled) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            CheckLobby();
        }

        private void CheckLobby()
        {
            try
            {
                var vm = FindPrivatelobbyVm();
                if (vm == null) return;

                var lobbyService = vm._lobbyService;
                var lobby = lobbyService?.Lobby;
                var members = lobby?._members;
                if (members == null) return;

                for (int i = 0; i < members.Count; i++)
                {
                    var member = members[i];
                    if (member == null) continue;

                    var memberId = member.Id;
                    var name = member.ExternalAccountName ?? "";
                    if (string.IsNullOrEmpty(memberId)) continue;
                    if (!BadName(name, LoadChecks())) continue;

                    if (_lastKick.TryGetValue(memberId, out var last) && Time.unscaledTime - last < 3f)
                        continue;

                    _lastKick[memberId] = Time.unscaledTime;
                    Plugin.Log?.LogWarning("[LobbyAutokickTweak] kicking bad name id=" + memberId + " name=" + SafeLabel(name));
                    SendKick(lobbyService, memberId);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError("[LobbyAutokickTweak] failed " + ex);
            }
        }

        public static string[] LoadChecks()
        {
            if (!int.TryParse(SettingsService.Get(CountKey, ""), out int count))
                return DefaultChecks;

            var checks = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string value = SettingsService.Get(CheckKey(i), "").Trim();
                if (!string.IsNullOrEmpty(value)) checks.Add(value);
            }

            return checks.ToArray();
        }

        public static void SaveChecks(IList<string> checks)
        {
            int oldCount = int.TryParse(SettingsService.Get(CountKey, "0"), out int old) ? old : 0;
            int count = checks == null ? 0 : checks.Count;
            SettingsService.Set(CountKey, count.ToString());

            for (int i = 0; i < count; i++)
                SettingsService.Set(CheckKey(i), checks[i] ?? "");
            for (int i = count; i < oldCount; i++)
                SettingsService.Remove(CheckKey(i));
        }

        public static void ResetChecks()
        {
            int oldCount = int.TryParse(SettingsService.Get(CountKey, "0"), out int old) ? old : 0;
            SaveChecks(DefaultChecks);
            for (int i = 0; i < oldCount; i++)
            {
                if (i < DefaultChecks.Length) continue;
                SettingsService.Remove(CheckKey(i));
            }
        }

        private static string CheckKey(int idx) => "tweak.lobby_autokick.contains." + idx;

        private static bool BadName(string name, string[] checks)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (checks == null || checks.Length == 0) return false;

            for (int i = 0; i < checks.Length; i++)
            {
                var check = checks[i];
                if (string.IsNullOrEmpty(check)) continue;
                if (name.IndexOf(check, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private void SendKick(FGClient.CatapultServices.ILobbyService lobbyService, string memberId)
        {
            var obs = lobbyService.HostKickMember(memberId);
            if (obs == null)
            {
                Plugin.Log?.LogWarning("[LobbyAutokickTweak] kick returned null " + memberId);
                return;
            }

            var onNext = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Result<LobbyDto>>>(
                new Action<Result<LobbyDto>>(OnKickResult));
            var onError = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Exception>>(
                new Action<Il2CppSystem.Exception>(OnKickError));

            obs.Subscribe(onNext, onError);
        }

        private void OnKickResult(Result<LobbyDto> result)
        {
            Plugin.Log?.LogInfo("[LobbyAutokickTweak] kick result " + (result == null ? "null" : result.ToString()));
        }

        private void OnKickError(Il2CppSystem.Exception ex)
        {
            Plugin.Log?.LogError("[LobbyAutokickTweak] kick failed " + (ex == null ? "null" : ex.Message));
        }

        private static FGClient.UI.PrivateLobby.PrivateLobbyScreenViewModel FindPrivatelobbyVm()
        {
            var views = Resources.FindObjectsOfTypeAll<FGClient.UI.PrivateLobby.PrivateLobbyScreenViewModel>();
            for (int i = 0; i < views.Length; i++)
            {
                var vm = views[i];
                if (vm == null || vm.gameObject == null) continue;
                if (!vm.gameObject.activeInHierarchy || !vm.isActiveAndEnabled) continue;
                if ((vm.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                if ((vm.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                if (!GetPath(vm.transform).Contains("/Default/")) continue;
                return vm;
            }

            return null;
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "null";
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private static string SafeLabel(string value)
        {
            value = (value ?? "").Replace("<", "[").Replace(">", "]");
            if (value.Length > 64) value = value.Substring(0, 64) + "...";
            return value;
        }
    }
#pragma warning restore CS8981
}
