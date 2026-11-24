using AxoClient.Common;
using AxoClient.DataModel.Instances;
using AxoClient.Instances.Contract;

namespace AxoClient.Instances
{
    /// <summary>Die Serverliste des Spiels (<c>servers.dat</c>), direkt gelesen und geschrieben.</summary>
    public sealed class ServerListManager : IServerListManager
    {
        private readonly AppPaths _paths;
        private readonly ITextHelper _textHelper;
        private readonly IAppLog _appLog;
        private readonly INbtReader _nbtReader;
        private readonly IServerPing _serverPing;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public ServerListManager(
            AppPaths paths,
            ITextHelper textHelper,
            IAppLog appLog,
            INbtReader nbtReader,
            IServerPing serverPing)
        {
            _paths = paths;
            _textHelper = textHelper;
            _appLog = appLog;
            _nbtReader = nbtReader;
            _serverPing = serverPing;
        }

        private string getFile(string id) => Path.Combine(_paths.GameDirectory(_textHelper.RequireId(id)), "servers.dat");

        public IReadOnlyList<ServerEntry> List(string instanceId)
        {
            var list = readList(instanceId);
            return list.Select((tag, index) => toEntry(instanceId, index, (NbtCompound)tag)).ToList();
        }

        /// <summary>Normalisierte Adressen aller Server einer Instanz ("Freund spielt auf deinem Server").</summary>
        public IReadOnlySet<string> NormalizedAddresses(string instanceId) =>
            List(instanceId).Select(server => _serverPing.Normalize(server.Address)).ToHashSet();

        private ServerEntry toEntry(string instanceId, int index, NbtCompound nbtCompound)
        {
            var icon = nbtCompound.Get<string>("icon");
            return new ServerEntry(index, _textHelper.Clean(nbtCompound.Get<string>("name"), 80), _textHelper.Clean(nbtCompound.Get<string>("ip"), 255),
                icon is { Length: > 0 } ? $"/axo/server-icon/{instanceId}/{index}" : null,
                nbtCompound.TryGetValue("acceptTextures", out var acceptTextures) && acceptTextures is sbyte flagValue && flagValue != 0);
        }

        /// <summary>Servericon als PNG (Base64 im NBT).</summary>
        public byte[]? Icon(string instanceId, int index)
        {
            var list = readList(instanceId);
            if (index < 0 || index >= list.Count || ((NbtCompound)list[index]).Get<string>("icon") is not { Length: > 0 } icon)
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(icon);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public async Task Add(string instanceId, string name, string address, int? atIndex = null)
        {
            validate(name, address);
            await change(instanceId, list =>
            {
                var entry = new NbtCompound { ["name"] = _textHelper.Clean(name, 80), ["ip"] = address.Trim() };
                if (atIndex is { } insertIndex && insertIndex >= 0 && insertIndex <= list.Count)
                {
                    list.Insert(insertIndex, entry);
                }
                else
                {
                    list.Add(entry);
                }
            });
        }

        public async Task Edit(string instanceId, int index, string name, string address)
        {
            validate(name, address);
            await change(instanceId, list =>
            {
                var entry = get(list, index);
                if (entry.Get<string>("ip") != address.Trim())
                {
                    entry.Remove("icon");
                }

                entry["name"] = _textHelper.Clean(name, 80);
                entry["ip"] = address.Trim();
            });
        }

        public Task Remove(string instanceId, int index) => change(instanceId, list =>
        {
            get(list, index);
            list.RemoveAt(index);
        });

        public Task Move(string instanceId, int from, int to) => change(instanceId, list =>
        {
            var entry = get(list, from);
            list.RemoveAt(from);
            list.Insert(Math.Clamp(to, 0, list.Count), entry);
        });

        private NbtCompound get(NbtList list, int index) =>
            index >= 0 && index < list.Count ? (NbtCompound)list[index] : throw new AxoException("errors.notFound");

        private void validate(string name, string address)
        {
            if (_textHelper.Clean(name, 80).Length == 0)
            {
                throw new AxoException("servers.nameMissing");
            }

            if (!_serverPing.IsValidAddress(address))
            {
                throw new AxoException("servers.addressInvalid");
            }
        }

        private NbtList readList(string instanceId)
        {
            var file = getFile(instanceId);
            if (!System.IO.File.Exists(file))
            {
                return new NbtList(10);
            }

            try
            {
                return _nbtReader.ReadFile(file).Get<NbtList>("servers") ?? new NbtList(10);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException)
            {
                _appLog.Warn("servers.dat nicht lesbar", exception);
                return new NbtList(10);
            }
        }

        private async Task change(string instanceId, Action<NbtList> change)
        {
            await _lock.WaitAsync();
            try
            {
                var file = getFile(instanceId);
                NbtCompound root;
                try
                {
                    root = System.IO.File.Exists(file) ? _nbtReader.ReadFile(file) : new NbtCompound();
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException)
                {
                    root = new NbtCompound();
                }
                var list = root.Get<NbtList>("servers") ?? new NbtList(10);
                change(list);
                root["servers"] = list;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                _nbtReader.WriteFile(file, root);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
