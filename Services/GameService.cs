using TicTacToe.Models;
using System.Security.Cryptography;
using System.Text;

namespace TicTacToe.Services;

public class GameService
{
    private readonly Dictionary<string, Room> _rooms = new();

    private string GenerateShortId()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = new byte[6];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var sb = new StringBuilder(6);
        foreach (var b in bytes)
            sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    public IEnumerable<Room> GetAvailableRooms() => _rooms.Values.Where(r => r.Players.Count < 2 || r.Players.Any(p => p.IsDisconnected));
    public IEnumerable<Room> GetAllRooms() => _rooms.Values;

    public Room? GetRoom(string roomId) => _rooms.GetValueOrDefault(roomId);

    public Room CreateRoom(string name, string connectionId)
    {
        var room = new Room
        {
            Id = GenerateShortId(),
            Name = name
        };
        room.Players.Add(new Player { ConnectionId = connectionId, Symbol = 'X' });
        _rooms[room.Id] = room;
        return room;
    }

    public bool JoinRoom(string roomId, string connectionId)
    {
        if (_rooms.TryGetValue(roomId, out var room) && !room.Players.Any(p => p.ConnectionId == connectionId))
        {
            if (room.Players.Count < 2)
            {
                var symbol = room.Players.Count == 0 ? 'X' : 'O';
                room.Players.Add(new Player { ConnectionId = connectionId, Symbol = symbol });
                return true;
            }
        }
        return false;
    }

    public void RemoveRoom(string roomId)
    {
        _rooms.Remove(roomId);
    }

    public void UpdateRoom(Room room)
    {
        if (_rooms.ContainsKey(room.Id))
        {
            _rooms[room.Id] = room;
        }
    }
}