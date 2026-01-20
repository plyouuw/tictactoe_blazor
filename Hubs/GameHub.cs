using Microsoft.AspNetCore.SignalR;
using TicTacToe.Models;
using TicTacToe.Services;

namespace TicTacToe.Hubs;

public class GameHub : Hub
{
    private readonly GameService _gameService;
    private readonly Dictionary<string, System.Timers.Timer> _disconnectTimers = new();
    private readonly ILogger<GameHub> _logger;

    public GameHub(GameService gameService, ILogger<GameHub> logger)
    {
        _gameService = gameService;
        _logger = logger;
    }

    public async Task CreateRoom(string roomName, int width, int height, int winCondition, bool hasAI, char aiSymbol)
    {
        _logger.LogInformation($"[CreateRoom] {Context.ConnectionId} tworzy: {roomName}, AI={hasAI}");
        try
        {
            var room = _gameService.CreateRoom(roomName, Context.ConnectionId);
            
            room.GameStartTime = DateTime.Now;
            room.Board.Width = width;
            room.Board.Height = height;
            room.WinCondition = winCondition;
            room.HasAI = hasAI;
            room.AISymbol = aiSymbol;
            
            // zaczyna losowy gracz
            Random rand = new Random();
            room.CurrentPlayer = rand.Next(2) == 0 ? 'X' : 'O';
            _logger.LogInformation($"[CreateRoom] Losowo wybrany pierwszy gracz: {room.CurrentPlayer}");
            
            if (hasAI)
            {
                room.Players.Add(new Player 
                { 
                    ConnectionId = "AI_BOT", 
                    Symbol = aiSymbol, 
                    Name = "AI Bot" 
                });
                _logger.LogInformation($"[CreateRoom] Dodano AI jako gracz {aiSymbol}");
            }
            
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Id);
            _gameService.UpdateRoom(room);
            
            _logger.LogInformation($"[CreateRoom] Pokój {room.Id} utworzony. Graczy: {room.Players.Count}");
            await Clients.Caller.SendAsync("RoomCreated", room);

            // wykonanie ruchu przez ai jesli w losowaniu wygra ze ono zaczyna
            if (hasAI && room.CurrentPlayer == aiSymbol)
            {
                _logger.LogInformation($"[CreateRoom] AI zaczyna - wykonuje pierwszy ruch...");
                await Task.Delay(500); // opoznienie ruchu ai, zeby nie bylo od razu 

                var ai = new AI(room.Board, 500);
                var aiPiece = aiSymbol == 'X' ? Piece.X : Piece.O;
                var move = ai.ChooseMove(aiPiece);
                
                if (room.Board.GetPiece(move.x, move.y) == Piece.None)
                {
                    room.Board.PlacePiece(move.x, move.y, aiPiece);
                    room.MoveCount++;
                    room.CurrentPlayer = room.CurrentPlayer == 'X' ? 'O' : 'X';
                    
                    _gameService.UpdateRoom(room);
                    await Clients.Group(room.Id).SendAsync("BoardUpdated", room);
                    _logger.LogInformation($"[CreateRoom] AI wykonało pierwszy ruch: (x={move.x}, y={move.y})");
                }
                else
                {
                    _logger.LogError($"[CreateRoom] AI wybrało zajęte pole ({move.x},{move.y})!");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[CreateRoom] BŁĄD: {ex.Message}");
        }
    }

    public async Task JoinRoom(string roomId)
    {
        _logger.LogInformation($"[JoinRoom] {Context.ConnectionId} dołącza do: {roomId}");
        try
        {
            var room = _gameService.GetRoom(roomId);
            if (room == null)
            {
                _logger.LogWarning($"[JoinRoom] Pokój {roomId} nie znaleziony!");
                await Clients.Caller.SendAsync("JoinFailed", "Room not found");
                return;
            }

            var connectionId = Context.ConnectionId;
            
            // sprawdzam czy gracz jest już w pokoju (sposob na buga z reconnect z nowym connectionid)
            if (room.Players.Any(p => p.ConnectionId == connectionId))
            {
                _logger.LogInformation($"[JoinRoom] Gracz już w pokoju");
                await Clients.Caller.SendAsync("PlayerJoined", room);
                return;
            }

            // jesli istnieje jakis ze statusem disconnected, to znaczy że byl reconnect!
            var disconnectedPlayer = room.Players.FirstOrDefault(p => p.IsDisconnected);
            if (disconnectedPlayer != null)
            {
                _logger.LogInformation($"[JoinRoom] Reconnect: Zamiana ConnectionId dla gracza {disconnectedPlayer.Symbol}");
                // wiec trzeba zmienic connectionid na nowe
                disconnectedPlayer.ConnectionId = connectionId;
                disconnectedPlayer.IsDisconnected = false;

                // timer daje czas na ponowne polaczenie dla gracza po jego utracie, jak sie nie zmiesci to gracz zostanie usuniety z pokoju
                // natomiast tutaj sie zmiescil wiec anulujemy timer
                var timerKey = room.Id + "_P" + room.Players.IndexOf(disconnectedPlayer);
                if (_disconnectTimers.TryGetValue(timerKey, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                    _disconnectTimers.Remove(timerKey);
                    _logger.LogInformation($"[JoinRoom] Anulowano timer disconnect");
                }
                
                _gameService.UpdateRoom(room);
                await Groups.AddToGroupAsync(connectionId, roomId);
                await Clients.Caller.SendAsync("PlayerJoined", room);
                await Clients.Group(roomId).SendAsync("PlayerJoined", room);
                _logger.LogInformation($"[JoinRoom] Reconnect zakończony");
                return;
            }

            if (room.Players.Count < 2)
            {
                var symbol = room.Players.Count == 0 ? 'X' : 'O';
                room.Players.Add(new Player { ConnectionId = connectionId, Symbol = symbol });
                _logger.LogInformation($"[JoinRoom] Gracz dodany jako '{symbol}'. Razem: {room.Players.Count}");
            }
            else
            {
                _logger.LogWarning($"[JoinRoom] Pokój pełny!");
                await Clients.Caller.SendAsync("JoinFailed", "Room full");
                return;
            }

            if (room.GameStartTime == null) room.GameStartTime = DateTime.Now;
            _gameService.UpdateRoom(room);
            await Groups.AddToGroupAsync(connectionId, roomId);
            await Clients.Caller.SendAsync("PlayerJoined", room);
            await Clients.Group(roomId).SendAsync("PlayerJoined", room);
            _logger.LogInformation($"[JoinRoom] Wysłano PlayerJoined do grupy");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[JoinRoom] BŁĄD: {ex.Message}");
        }
    }

    public async Task JoinRoomReconnect(string roomId, string playerSymbol)
    {
        _logger.LogInformation($"[JoinRoomReconnect] {Context.ConnectionId} reconnect do: {roomId} jako {playerSymbol}");
        try
        {
            var room = _gameService.GetRoom(roomId);
            if (room == null)
            {
                _logger.LogWarning($"[JoinRoomReconnect] Pokój {roomId} nie znaleziony!");
                await Clients.Caller.SendAsync("JoinFailed", "Room not found");
                return;
            }

            var connectionId = Context.ConnectionId;
            char symbol = playerSymbol.Length > 0 ? playerSymbol[0] : 'X';
            
            var existingPlayer = room.Players.FirstOrDefault(p => p.Symbol == symbol);
            if (existingPlayer != null)
            {
                _logger.LogInformation($"[JoinRoomReconnect] Znaleziono gracza {symbol}, zamiana ConnectionId {existingPlayer.ConnectionId} → {connectionId}");

                var oldConnectionId = existingPlayer.ConnectionId;
                existingPlayer.ConnectionId = connectionId;
                existingPlayer.IsDisconnected = false;
                
                try
                {
                    await Groups.RemoveFromGroupAsync(oldConnectionId, roomId);
                }
                catch { /* jeśli już nie istnieje to pomijam */ }
                
                await Groups.AddToGroupAsync(connectionId, roomId);
                
                _gameService.UpdateRoom(room);
                await Clients.Caller.SendAsync("PlayerJoined", room);
                await Clients.Group(roomId).SendAsync("PlayerJoined", room);
                _logger.LogInformation($"[JoinRoomReconnect] ✓ Reconnect zakończony");
                return;
            }
            
            _logger.LogWarning($"[JoinRoomReconnect] Nie znaleziono gracza {symbol}, dołączanie jako nowy...");
            await JoinRoom(roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"[JoinRoomReconnect] BŁĄD: {ex.Message}");
        }
    }

    public async Task MakeMove(string roomId, int x, int y)
    {
        _logger.LogInformation($"[MakeMove] {Context.ConnectionId} ruch: ({x},{y}) w {roomId}");
        try
        {
            var room = _gameService.GetRoom(roomId);
            if (room == null)
            {
                _logger.LogWarning($"[MakeMove] Pokój nie znaleziony!");
                return;
            }

            if (room.GameResult != "Gra trwa" || room.IsPaused || room.RoundFinished)
            {
                _logger.LogWarning($"[MakeMove] Nieprawidłowy stan gry!");
                return;
            }

            var currentPlayer = room.CurrentPlayer;
            var piece = currentPlayer == 'X' ? Piece.X : Piece.O;

            if (room.Board.GetPiece(x, y) != Piece.None)
            {
                _logger.LogWarning($"[MakeMove] Pole zajęte!");
                return;
            }

            room.Board.PlacePiece(x, y, piece);
            room.MoveCount++;
            
            _logger.LogInformation($"[MakeMove] Ruch zatwierdzony. MoveCount={room.MoveCount}");

            if (room.Board.IsWin(x, y, piece, room.WinCondition))
            {
                if (!room.Wins.ContainsKey(currentPlayer))
                    room.Wins[currentPlayer] = 0;
                room.Wins[currentPlayer]++;
                room.GameResult = $"Wygrywa {currentPlayer}";
                room.RoundFinished = true;
                room.LastWinner = currentPlayer;
                _logger.LogInformation($"[MakeMove] 🎉 Wygrana! Gracz {currentPlayer}");
            }
            else if (room.MoveCount >= room.Board.Width * room.Board.Height)
            {
                room.Draws++;
                room.GameResult = "Remis";
                room.RoundFinished = true;
                _logger.LogInformation($"[MakeMove] 🤝 Remis!");
            }
            else
            {
                room.CurrentPlayer = room.CurrentPlayer == 'X' ? 'O' : 'X';
                _logger.LogInformation($"[MakeMove] Następny gracz: {room.CurrentPlayer}");
            }

            _gameService.UpdateRoom(room);
            await Clients.Group(roomId).SendAsync("BoardUpdated", room);

            // ruch ai
            if (room.HasAI && !room.RoundFinished && room.CurrentPlayer == room.AISymbol)
            {
                _logger.LogInformation($"[MakeMove] AI wykonuje ruch ({room.AISymbol})...");
                await Task.Delay(500);
                
                var ai = new AI(room.Board, 500);
                var aiPiece = room.AISymbol == 'X' ? Piece.X : Piece.O;
                var move = ai.ChooseMove(aiPiece);
                
                if (room.Board.GetPiece(move.x, move.y) == Piece.None)
                {
                    room.Board.PlacePiece(move.x, move.y, aiPiece);
                    room.MoveCount++;
                    _logger.LogInformation($"[MakeMove] AI ruch: x={move.x}, y={move.y}");

                    if (room.Board.IsWin(move.x, move.y, aiPiece, room.WinCondition))
                    {
                        if (!room.Wins.ContainsKey(room.AISymbol))
                            room.Wins[room.AISymbol] = 0;
                        room.Wins[room.AISymbol]++;
                        room.GameResult = $"Wygrywa {room.AISymbol}";
                        room.RoundFinished = true;
                        room.LastWinner = room.AISymbol;
                        _logger.LogInformation($"[MakeMove] 🤖 AI wygrała!");
                    }
                    else if (room.MoveCount >= room.Board.Width * room.Board.Height)
                    {
                        room.Draws++;
                        room.GameResult = "Remis";
                        room.RoundFinished = true;
                        _logger.LogInformation($"[MakeMove] 🤖 Remis po ruchu AI!");
                    }
                    else
                    {
                        room.CurrentPlayer = room.CurrentPlayer == 'X' ? 'O' : 'X';
                    }

                    _gameService.UpdateRoom(room);
                    await Clients.Group(roomId).SendAsync("BoardUpdated", room);
                }
                else
                {
                    _logger.LogError($"[MakeMove] AI wybrało zajęte pole ({move.x},{move.y})!");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[MakeMove] BŁĄD: {ex.Message}");
        }
    }

    public async Task PauseGame(string roomId)
    {
        _logger.LogInformation($"[PauseGame] {Context.ConnectionId} pauzuje grę {roomId}");
        var room = _gameService.GetRoom(roomId);
        if (room != null)
        {
            room.IsPaused = true;
            room.PausedBy = Context.ConnectionId;
            _gameService.UpdateRoom(room);
            await Clients.Group(roomId).SendAsync("GamePaused", room);
            _logger.LogInformation($"[PauseGame] Gra wstrzymana");
        }
    }

    public async Task ResumeGame(string roomId)
    {
        _logger.LogInformation($"[ResumeGame] {Context.ConnectionId} wznawia grę {roomId}");
        var room = _gameService.GetRoom(roomId);
        if (room != null)
        {
            room.IsPaused = false;
            room.PausedBy = null;
            _gameService.UpdateRoom(room);
            await Clients.Group(roomId).SendAsync("GameResumed", room);
            _logger.LogInformation($"[ResumeGame] Gra wznowiona");
        }
    }

    public async Task NextGame(string roomId)
    {
        _logger.LogInformation($"[NextGame] {Context.ConnectionId} rozpoczyna następną rundę {roomId}");
        var room = _gameService.GetRoom(roomId);
        if (room != null && room.RoundFinished)
        {
            room.ResetBoard();
            room.MoveCount = 0;
            
            // przegrany zaczyna
            if (room.LastWinner.HasValue && room.GameResult != "Remis")
            {
                room.CurrentPlayer = room.LastWinner.Value == 'X' ? 'O' : 'X';
                _logger.LogInformation($"[NextGame] Przegrany zaczyna: {room.CurrentPlayer} (zwycięzca: {room.LastWinner})");
            }
            else
            {
                // remis lub brak zwycięzcy - losuj
                Random rand = new Random();
                room.CurrentPlayer = rand.Next(2) == 0 ? 'X' : 'O';
                _logger.LogInformation($"[NextGame] Remis/losuj - zaczyna: {room.CurrentPlayer}");
            }
            
            room.GameResult = "Gra trwa";
            room.RoundNumber++;
            room.RoundFinished = false;
            _gameService.UpdateRoom(room);
            await Clients.Group(roomId).SendAsync("BoardUpdated", room);
            _logger.LogInformation($"[NextGame] Runda {room.RoundNumber} rozpoczęta");

            // ten sam kod co przy tworzeniu pokoju
            if (room.HasAI && room.CurrentPlayer == room.AISymbol)
            {
                _logger.LogInformation($"[NextGame] AI zaczyna nową rundę - wykonuje pierwszy ruch...");
                await Task.Delay(500);
                
                var ai = new AI(room.Board, 500);
                var aiPiece = room.AISymbol == 'X' ? Piece.X : Piece.O;
                var move = ai.ChooseMove(aiPiece);
                
                if (room.Board.GetPiece(move.x, move.y) == Piece.None)
                {
                    room.Board.PlacePiece(move.x, move.y, aiPiece);
                    room.MoveCount++;
                    room.CurrentPlayer = room.CurrentPlayer == 'X' ? 'O' : 'X';
                    
                    _gameService.UpdateRoom(room);
                    await Clients.Group(roomId).SendAsync("BoardUpdated", room);
                    _logger.LogInformation($"[NextGame] AI wykonało pierwszy ruch nowej rundy: (x={move.x}, y={move.y})");
                }
                else
                {
                    _logger.LogError($"[NextGame] AI wybrało zajęte pole ({move.x},{move.y})!");
                }
            }
        }
    }

    public async Task LeaveGame(string roomId)
    {
        _logger.LogInformation($"[LeaveGame] {Context.ConnectionId} opuszcza pokój {roomId}");
        var room = _gameService.GetRoom(roomId);
        if (room != null)
        {
            var connectionId = Context.ConnectionId;
            var playerIndex = room.Players.FindIndex(p => p.ConnectionId == connectionId);
            
            if (playerIndex >= 0)
            {
                room.Players.RemoveAt(playerIndex);
                _logger.LogInformation($"[LeaveGame] Gracz usunięty. Pozostało: {room.Players.Count}");
            }
            else if (room.Spectators.Contains(connectionId))
            {
                room.Spectators.Remove(connectionId);
                _logger.LogInformation($"[LeaveGame] Obserwator usunięty");
            }

            // jesli odchodzi gracz z pokoju z wlaczonym ai, to ai tez musi wyjsc z pokoju, zeby ten przestal istniec
            if (room.HasAI && room.Players.Count == 1)
            {
                var aiPlayer = room.Players.FirstOrDefault(p => p.ConnectionId == "AI_BOT");
                if (aiPlayer != null)
                {
                    room.Players.Remove(aiPlayer);
                    _logger.LogInformation($"[LeaveGame] Usunięto AI, pokój będzie zamknięty");
                }
            }

            if (room.Players.Count == 0)
            {
                _gameService.RemoveRoom(room.Id);
                await Clients.Group(roomId).SendAsync("RoomClosed");
                _logger.LogInformation($"[LeaveGame] Pokój {roomId} zamknięty (brak graczy)");
            }
            else
            {
                _gameService.UpdateRoom(room);
                await Clients.Group(roomId).SendAsync("PlayerLeft", room);
            }

            await Clients.Caller.SendAsync("LeftGame");
        }
    }

    public async Task SpectateRoom(string roomId)
    {
        _logger.LogInformation($"[SpectateRoom] {Context.ConnectionId} obserwuje pokój {roomId}");
        var room = _gameService.GetRoom(roomId);
        if (room != null)
        {
            room.Spectators.Add(Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            await Clients.Caller.SendAsync("SpectatorJoined", room);
            _logger.LogInformation($"[SpectateRoom] Obserwator dołączył");
        }
        else
        {
            _logger.LogWarning($"[SpectateRoom] Pokój {roomId} nie znaleziony!");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"[Disconnect] {Context.ConnectionId} rozłączył się");
        var connectionId = Context.ConnectionId;
        var allRooms = _gameService.GetAllRooms();
        
        foreach (var room in allRooms.Where(r => r.Players.Any(p => p.ConnectionId == connectionId)))
        {
            var playerIndex = room.Players.FindIndex(p => p.ConnectionId == connectionId);
            if (playerIndex >= 0)
            {
                room.Players[playerIndex].IsDisconnected = true;
                _logger.LogInformation($"[Disconnect] Gracz {playerIndex} (symbol: {room.Players[playerIndex].Symbol}) w pokoju {room.Id} oznaczony jako rozłączony");
                
                var timerKey = room.Id + "_P" + playerIndex;
                var timer = new System.Timers.Timer(10000);
                timer.Elapsed += async (sender, e) =>
                {
                    _logger.LogInformation($"[Disconnect] Timer 10s upłynął - usuwanie gracza z pokoju {room.Id}");
                    var r = _gameService.GetRoom(room.Id);
                    if (r != null)
                    {
                        var idx = r.Players.FindIndex(p => p.ConnectionId == connectionId && p.IsDisconnected);
                        if (idx >= 0)
                        {
                            r.Players.RemoveAt(idx);
                            _logger.LogInformation($"[Disconnect] Gracz usunięty. Pozostało: {r.Players.Count}");
                        }

                        // usuwanie pokoju z samym ai bez gracza
                        if (r.HasAI && r.Players.Count == 1)
                        {
                            var aiPlayer = r.Players.FirstOrDefault(p => p.ConnectionId == "AI_BOT");
                            if (aiPlayer != null)
                            {
                                r.Players.Remove(aiPlayer);
                                _logger.LogInformation($"[Disconnect] Usunięto AI po disconnect gracza");
                            }
                        }

                        if (r.Players.Count == 0)
                        {
                            _gameService.RemoveRoom(r.Id);
                            await Clients.Group(r.Id).SendAsync("RoomClosed");
                            _logger.LogInformation($"[Disconnect] Pokój {r.Id} zamknięty (brak graczy)");
                        }
                        else
                        {
                            _gameService.UpdateRoom(r);
                            await Clients.Group(r.Id).SendAsync("PlayerLeft", r);
                        }
                    }
                    timer.Stop();
                    timer.Dispose();
                    _disconnectTimers.Remove(timerKey);
                };
                
                _disconnectTimers[timerKey] = timer;
                timer.Start();
                _logger.LogInformation($"[Disconnect] Timer 10s uruchomiony dla gracza w pokoju {room.Id}");
            }

            _gameService.UpdateRoom(room);
            await Clients.Group(room.Id).SendAsync("PlayerLeft", room);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
