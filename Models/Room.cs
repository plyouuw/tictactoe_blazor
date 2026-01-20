namespace TicTacToe.Models;

public class Player
{
    public string ConnectionId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public char Symbol { get; set; }
    public bool IsDisconnected { get; set; } = false;
}

public class Room
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<Player> Players { get; set; } = [];
    public List<string> Spectators { get; set; } = [];
    public GameBoard Board { get; set; } = new GameBoard();
    public char CurrentPlayer { get; set; } = 'X';
    public string GameResult { get; set; } = "Gra trwa";
    public int RoundNumber { get; set; } = 1;
    public Dictionary<char, int> Wins { get; set; } = new Dictionary<char, int> { ['X'] = 0, ['O'] = 0 };
    public int Draws { get; set; } = 0;
    public char? LastWinner { get; set; } = null;
    public DateTime? GameStartTime { get; set; }
    public TimeSpan TotalDuration { get; set; } = TimeSpan.Zero;
    public bool IsPaused { get; set; } = false;
    public string? PausedBy { get; set; } // ConnectionId
    public bool IsFinished { get; set; } = false;
    public bool RoundFinished { get; set; } = false;
    public int WinCondition { get; set; } = 3; // ile znakow w jednej linii zeby uzac wygrana
    public int MoveCount { get; set; } = 0; // jak sie gra zakonczy z taka sama liczba to remis
    public bool HasAI { get; set; } = false;
    public char AISymbol { get; set; } = 'O';

    public void ResetBoard()
    {
        Board.ResetBoard();
    }
}
public class GameMove
{
    public int X { get; set; }
    public int Y { get; set; }
    public Piece Piece { get; set; }
}

public class GameBoard
{
    public List<GameMove> Moves { get; set; } = [];
    public int Width { get; set; } = 3;
    public int Height { get; set; } = 3;

    public Piece GetPiece(int x, int y) =>
        Moves.FirstOrDefault(m => m.X == x && m.Y == y)?.Piece ?? Piece.None;

    public void PlacePiece(int x, int y, Piece piece)
    {
        var existing = Moves.FirstOrDefault(m => m.X == x && m.Y == y);
        if (existing != null)
            existing.Piece = piece;
        else
            Moves.Add(new GameMove { X = x, Y = y, Piece = piece });
    }

    public bool IsWin(int x, int y, Piece piece, int winCondition = 3)
    {
        var directions = new[] { (1, 0), (0, 1), (1, 1), (1, -1) };
        foreach (var (dx, dy) in directions)
        {
            int count = 1;
            for (int i = 1; i < winCondition; i++)
            {
                if (GetPiece(x + i * dx, y + i * dy) == piece) count++;
                else break;
            }
            for (int i = 1; i < winCondition; i++)
            {
                if (GetPiece(x - i * dx, y - i * dy) == piece) count++;
                else break;
            }
            if (count >= winCondition) return true;
        }
        return false;
    }

    public void ResetBoard()
    {
        Moves.Clear();
    }

    public List<(int X, int Y)> GetWinningLine(int x, int y, Piece piece, int winCondition = 3)
    {
        var line = new List<(int X, int Y)>();
        var directions = new[] { (1, 0), (0, 1), (1, 1), (1, -1) };
        foreach (var (dx, dy) in directions)
        {
            var tempLine = new List<(int X, int Y)> { (x, y) };
            for (int i = 1; i < winCondition; i++)
            {
                var nx = x + i * dx;
                var ny = y + i * dy;
                if (GetPiece(nx, ny) == piece) tempLine.Add((nx, ny));
                else break;
            }
            for (int i = 1; i < winCondition; i++)
            {
                var nx = x - i * dx;
                var ny = y - i * dy;
                if (GetPiece(nx, ny) == piece) tempLine.Insert(0, (nx, ny));
                else break;
            }
            if (tempLine.Count >= winCondition)
            {
                line = tempLine;
                break;
            }
        }
        return line;
    }
}

public enum Piece { None, X, O }

public class AI
{
    private GameBoard Board;
    private int Width;
    private int Height;
    private int Delay;

    public AI(GameBoard board, int delay)
    {
        Board = board;
        Width = board.Width;
        Height = board.Height;
        Delay = delay;
    }

    private Piece OppositePiece(Piece piece)
    {
        if (piece == Piece.X) return Piece.O;
        else if (piece == Piece.O) return Piece.X;
        else return Piece.None;
    }

    private (int x, int y, bool effective) FindNearestCombination(int x, int y, Piece piece, int winCondition = 3)
    {
        var directions = new[] { (1, 0), (0, 1), (1, 1), (1, -1) };
        foreach (var (dx, dy) in directions)
        {
            int count = 1;
            for (int i = 1; i < winCondition; i++)
            {
                if (Board.GetPiece(x + i * dx, y + i * dy) == piece) count++;
                else break;
            }
            for (int i = 1; i < winCondition; i++)
            {
                if (Board.GetPiece(x - i * dx, y - i * dy) == piece) count++;
                else break;
            }
            if (count >= winCondition - 1)
            {
                for (int i = 1; i < winCondition; i++)
                {
                    var nx = x + i * dx;
                    var ny = y + i * dy;
                    if (nx >= 0 && nx < Width && ny >= 0 && ny < Height && Board.GetPiece(nx, ny) == Piece.None)
                        return (nx, ny, true);
                    nx = x - i * dx;
                    ny = y - i * dy;
                    if (nx >= 0 && nx < Width && ny >= 0 && ny < Height && Board.GetPiece(nx, ny) == Piece.None)
                        return (nx, ny, true);
                }
            }
        }
        return (-1, -1, false);
    }

    private int EvaluateMove(Piece piece)
    {
        bool empty = true;
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (Board.GetPiece(x, y) != Piece.None) empty = false;
            }
        }

        if (empty) return 2; // przypadkowe pole

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (Board.GetPiece(x, y) == OppositePiece(piece))
                    if (FindNearestCombination(x, y, OppositePiece(piece)).effective)
                        return 1; // blokuj przeciwnika
                if (Board.GetPiece(x, y) == piece)
                    if (FindNearestCombination(x, y, piece).effective)
                        return 0; // a tu atakuj
            }
        }
        return 2;
    }

    public (int x, int y) ChooseMove(Piece piece)
    {
        int x = -1, y = -1;
        int strategy = EvaluateMove(piece);
        
        if (strategy == 0) // atakuj
        {
            for (int xi = 0; xi < Width; xi++)
            {
                for (int yi = 0; yi < Height; yi++)
                {
                    if (Board.GetPiece(xi, yi) == piece)
                    {
                        var move = FindNearestCombination(xi, yi, piece);
                        if (move.effective)
                        {
                            x = move.x;
                            y = move.y;
                            goto found;
                        }
                    }
                }
            }
        }
        else if (strategy == 1) // blokuj
        {
            for (int xi = 0; xi < Width; xi++)
            {
                for (int yi = 0; yi < Height; yi++)
                {
                    if (Board.GetPiece(xi, yi) == OppositePiece(piece))
                    {
                        var move = FindNearestCombination(xi, yi, OppositePiece(piece));
                        if (move.effective)
                        {
                            x = move.x;
                            y = move.y;
                            goto found;
                        }
                    }
                }
            }
        }
        else // przypadkowe pole na calej planszy, chociaz mozna tutaj dodac zeby bylo najblizsze ostatniego znaku gracza w odstêpie min 1 kratka w kazda strone (chyba ze wielkosc planszy nie pozwala)
        {
            Random rand = new();
            var freeSpaces = new List<(int x, int y)>();
            for (int xi = 0; xi < Width; xi++)
            {
                for (int yi = 0; yi < Height; yi++)
                {
                    if (Board.GetPiece(xi, yi) == Piece.None)
                    {
                        freeSpaces.Add((xi, yi));
                    }
                }
            }
            
            if (freeSpaces.Count > 0)
            {
                var randomSpace = freeSpaces[rand.Next(freeSpaces.Count)];
                x = randomSpace.x;
                y = randomSpace.y;
            }
        }
    found: // wyjscie z podwojnej petli
        if (x == -1 || y == -1 || x >= Width || y >= Height || Board.GetPiece(x, y) != Piece.None)
        {
            Random rand = new();
            var freeSpaces = new List<(int x, int y)>();
            for (int xi = 0; xi < Width; xi++)
            {
                for (int yi = 0; yi < Height; yi++)
                {
                    if (Board.GetPiece(xi, yi) == Piece.None)
                    {
                        freeSpaces.Add((xi, yi));
                    }
                }
            }
            
            if (freeSpaces.Count > 0)
            {
                var randomSpace = freeSpaces[rand.Next(freeSpaces.Count)];
                x = randomSpace.x;
                y = randomSpace.y;
            }
            else
            {
                // gdyby sie mialo zdazyc, ze petla sie wykona na remisie, to zeby nie walilo wyjatkiem robie taki fallback
                x = 0;
                y = 0;
            }
        }
        if (Board.GetPiece(x, y) != Piece.None)
        {
            // szukam pierwszego wolnego pola
            for (int xi = 0; xi < Width; xi++)
            {
                for (int yi = 0; yi < Height; yi++)
                {
                    if (Board.GetPiece(xi, yi) == Piece.None)
                    {
                        Thread.Sleep(Delay);
                        return (xi, yi);
                    }
                }
            }
            // jakby remis, jak wczesniej
            x = 0;
            y = 0;
        }
        Thread.Sleep(Delay);
        return (x, y);
    }
}