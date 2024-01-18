// Heyya, sorry for making another complications to the rules sweat_smile.

// But it is still possible to reduce the size of the program (I think, haven't tested it that much :).
// It is possible to do it by converting the program to hex (https://gchq.github.io/CyberChef/#recipe=To_Base64('A-Za-z0-9%2B/%3D')To_Hex('Space',0)&input=TW92ZVtdIG1vdmVzID0gYm9hcmQuR2V0TGVnYWxNb3Zlcygp).

// Then, take as many of the hex chars and put them in a hex number (can be at most 16 hex digits) -> which then can be converted back to arbitrary strings:

// ulong[] hexNumbers = new ulong[]
// {
// 0x545739325a567464, 0x49473176646d567a,
// };

// List hexBytesList = new List();

// foreach (ulong hexNumber in hexNumbers)
// {
// byte[] bytes = BitConverter.GetBytes(hexNumber);
// if (BitConverter.IsLittleEndian)
// Array.Reverse(bytes); // ensures bytes are in the correct order
// hexBytesList.AddRange(bytes);
// }

// byte[] hexBytes = hexBytesList.ToArray();

// string ascii = Encoding.ASCII.GetString(hexBytes);

// byte[] data = Convert.FromBase64String(ascii);
// string decodedString = Encoding.UTF8.GetString(data);

// Console.WriteLine(decodedString);

// I don't see any good way to change the Token counter to prevent this.






























using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    const int maxSearchDepth = 10;
    int defaultSearchDepth = 4;

    Move[][] allPossibleMoves = new Move[maxSearchDepth][];
    Move bestMove;

    public MyBot()
    {
        for (int i = 0; i < maxSearchDepth; i++) {
            allPossibleMoves[i] = new Move[218];
        }
    }

    public Move Think(Board board, Timer timer)
    {
        Console.WriteLine(board.ZobristKey);

        Span<Move> moves = allPossibleMoves[defaultSearchDepth].AsSpan();
        moves.Clear();
        board.GetLegalMovesNonAlloc(ref moves);
        bestMove = moves[0];
        MoveCalculation(board, defaultSearchDepth, true);

        board.MakeMove(bestMove);
        if (board.IsDraw())
            Console.WriteLine("ABOUT TO MAKE A MOVE THAT RESULTS IN A DRAW.");
        board.UndoMove(bestMove);

        Console.WriteLine(board.ZobristKey);
        return bestMove; 
    }

    public float MoveCalculation(Board board, int depthleft, bool setBestMove)
    {
        if (depthleft == 0)
            return EvaluatePosition(board);

        Span<Move> moves = allPossibleMoves[depthleft].AsSpan();
        board.GetLegalMovesNonAlloc(ref moves);
        float bestScore = float.MinValue;
        foreach (Move move in moves) {
            board.MakeMove(move);
            float score = -MoveCalculation(board, depthleft - 1, false);
            board.UndoMove(move);
            if (score > bestScore)
            {
                bestScore = score;
                if (setBestMove)
                {
                    bestMove = move;
                }
            }
        }
        return bestScore;
    }

    public float EvaluatePosition(Board board)
    {
        float evaluation = 0;

        // If draw:
        if (board.IsDraw())
            return float.MaxValue; // Essentially treat being in a draw as a win if it is your turn, making sure the opponent should never allow a position where a draw is possible.

        // If checkmate:
        if (board.IsInCheckmate())
            return float.MinValue; // Very bad outcome for the player who is checkmated so assigning a very negative score.

        return evaluation;
    }
}



























































using System;
using ChessChallenge.API;

public class MyBot : IChessBot
{
private int maxDepth = 3;
private Move bestMove;
private Board board;
// Point values for each piece type for evaluation
int[] pointValues = {100, 320, 330, 500, 900, 99999};
// Big table packed with data from premade piece square tables

private readonly ulong[,] PackedEvaluationTables = {
    { 58233348458073600, 61037146059233280, 63851895826342400, 66655671952007680 },
    { 63862891026503730, 66665589183147058, 69480338950193202, 226499563094066 },
    { 63862895153701386, 69480338782421002, 5867015520979476,  8670770172137246 },
    { 63862916628537861, 69480338782749957, 8681765288087306,  11485519939245081 },
    { 63872833708024320, 69491333898698752, 8692760404692736,  11496515055522836 },
    { 63884885386256901, 69502350490469883, 5889005753862902,  8703755520970496 },
    { 63636395758376965, 63635334969551882, 21474836490,       1516 },
    { 58006849062751744, 63647386663573504, 63625396431020544, 63614422789579264 }
};

public int GetSquareBonus(PieceType type, bool isWhite, int file, int rank)
{
    // Because arrays are only 4 squares wide, mirror across files
    if (file > 3)
        file = 7 - file;

    // Mirror vertically for white pieces, since piece arrays are flipped vertically
    if (isWhite)
        rank = 7 - rank;

    // First, shift the data so that the correct byte is sitting in the least significant position
    // Then, mask it out
    // Use unchecked to preserve the sign in case of an overflow
    sbyte unpackedData = unchecked((sbyte)((PackedEvaluationTables[rank, file] >> 8 * ((int)type - 1)) & 0xFF));

    // Invert eval scores for black pieces
    return isWhite ? unpackedData : -unpackedData;
}

// Negamax algorithm with alpha-beta pruning
public int Search(int depth, int alpha, int beta, int color)
{
    // If the search reaches the desired depth or the end of the game, evaluate the position and return its value
    if (depth == 0 || board.IsDraw() || board.IsInCheckmate())
    {
        if (board.IsDraw()) return 0;
        
        if (board.IsInCheckmate()) return -999999 + (maxDepth - depth);
        
        return EvaluateBoard();
    }
    Move[] legalMoves = board.GetLegalMoves();
    Random random = new Random();
    int bestEval = -999999;
    int eval;
    // Generate and loop through all legal moves for the current player
    foreach(Move move in legalMoves)
    {
        // Make the move on a temporary board and call search recursively
        board.MakeMove(move);
        eval = -Search(depth - 1, -beta, -alpha, -color);
        board.UndoMove(move);

        // Update the best move and prune if necessary
        if (eval > bestEval)
        {
            bestEval = eval;
            if (depth == maxDepth)
            {   
                bestMove = move;
            }
            // Improve alpha
            alpha = Math.Max(alpha, eval);
            
            if (alpha >= beta) break;
            
        }
    }
    
    return bestEval;
}

public int EvaluateBoard()
{
    int materialValue = 0;
    int mobilityValue = board.GetLegalMoves().Length;
    PieceList[] pieceLists = board.GetAllPieceLists();
    int color = board.IsWhiteToMove ? 1 : -1;
    int pieceCount = 0;
    // Loop through each piece type and add the difference in material value to the total
    int squereBonus = 0;
    foreach(PieceList pList in pieceLists)
    {
        pieceCount += pList.Count;
    }
    
    if(pieceCount<= 10)
    {
        maxDepth = 5;
    }
    foreach(PieceList pList in pieceLists)
    {
        foreach(Piece piece in pList)
        {
            squereBonus += GetSquareBonus(piece.PieceType,piece.IsWhite,piece.Square.File, piece.Square.Rank);
        }
    }
    for(int i = 0;i < 5; i++){
        materialValue += (pieceLists[i].Count - pieceLists[i + 6].Count) * pointValues[i];
    }
    return materialValue * color + mobilityValue * color + squereBonus;
}
public Move Think(Board board, Timer timer)
{
    this.board = board;
    // Call the Minimax algorithm to find the best move
    Console.WriteLine(Search(maxDepth, -999999, 999999, board.IsWhiteToMove ? 1 : -1) + "  " + bestMove + " is white turn: " + board.IsWhiteToMove);
    return bestMove;
}







































































using ChessChallenge.API;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics;
using System.Linq;
namespace ChessChallenge.Example;
public class EvilBot : IChessBot
{
public int[] pieceVals = { 0, 100, 300, 320, 500, 900, 10000 }; // nothing, pawn, knight, bishop, rook, queen, king
///

/// Blind Bot:
/// Probably the best chess bot that CANNOT look ahead!
/// This was a fun challenge!
/// This bot can only check evaluation for the current move using a very complex hand-made evaluation function
/// This took a while!
/// The most major advantage is that it finishes each move in ~5 ms.
/// In the massive fight, this can only win against bots who check up to like 30 moves.
/// However I think this would be an intresting experiment and would be fun for the grand finale video.

/// </summary>
int movesSinceLastPawnMove = 0;

int kingDSTfromOpponentKing(Board board)
{
    Square myKingSquare = board.GetKingSquare(board.IsWhiteToMove);
    Square oKingSquare = board.GetKingSquare(!board.IsWhiteToMove);
    int fileDist = Math.Abs(myKingSquare.File - oKingSquare.File);
    int rankDist = Math.Abs(myKingSquare.Rank - oKingSquare.Rank);
    int dst = fileDist + rankDist;
    return dst;
}

public Move[] GetEnemyMoves(Board board)
{
    board.MakeMove(Move.NullMove);
    Move[] enemyMoves = board.GetLegalMoves();
    board.UndoMove(Move.NullMove);
    return enemyMoves;
}

int piecesLeft(Board board)
{
    int count = 0;
    for (var i = 0; i < 64; i++)
        if (board.GetPiece(new Square(i)).PieceType != PieceType.None) count++;
    return count;
}

bool moveIsCheckmate(Board board, Move move)
{
    board.MakeMove(move);
    bool isMate = board.IsInCheckmate();
    board.UndoMove(move);
    return isMate;
}

bool moveIsCheck(Board board, Move move)
{
    board.MakeMove(move);
    bool isCheck = board.IsInCheck();
    board.UndoMove(move);
    return isCheck;
}


bool moveHasBeenPlayed(Board board, Move move) 
{
    board.MakeMove(move);
    bool hasBeenPlayed = board.IsDraw();
    board.UndoMove(move);
    return hasBeenPlayed;
}


int evaluateMove(Move move, Board board) // evaluates the move
{
    int piecesLeftnow = piecesLeft(board);
    PieceType capturedPiece = move.CapturePieceType;
    int eval = 0;
    eval = pieceVals[(int)capturedPiece];
    if (eval > 0) { eval += 5; }
    if (board.SquareIsAttackedByOpponent(move.TargetSquare)) // uh oh here come the piece square tables
    {
        eval -= pieceVals[(int)move.MovePieceType];
    }
    ///<summary>
    /// Piece square tables for all pieces except king.
    /// This also includes that you should push out your queen early game.
    /// This will priortise "good" moves like castling and promoting over worse moves.
    /// It will also transition into endgame tables where your queen and rook are more important.
    /// This is responsible for more than half of the tokens BTW.
    /// </summary>
    if (move.MovePieceType == PieceType.Knight)// Knight piece square table, will prefer to be in the middle.
    {
        eval += 15;
        if (move.TargetSquare.File == 7 || move.TargetSquare.File == 6 || move.TargetSquare.File == 0 || move.TargetSquare.File == 1) eval -= 60;
        if (move.TargetSquare.File == 2 || move.TargetSquare.File == 3 || move.TargetSquare.File == 4 || move.TargetSquare.File == 5) if (move.TargetSquare.Rank == 2 || move.TargetSquare.Rank == 3 || move.TargetSquare.Rank == 4 || move.TargetSquare.Rank == 5) eval += 45;
    }
    if (move.MovePieceType == PieceType.Bishop)// Bishop piece square table
    {
        if (piecesLeftnow > 28) eval -= 30;
        eval += 15;
        if (move.TargetSquare.File == 2 || move.TargetSquare.File == 3 || move.TargetSquare.File == 4 || move.TargetSquare.File == 5)
        {
            if (move.TargetSquare.Rank == 2 || move.TargetSquare.Rank == 3 || move.TargetSquare.Rank == 4 || move.TargetSquare.Rank == 5) eval += 45;
        }
    }
    if (move.MovePieceType == PieceType.Rook)// Rook piece square table + transition to endgame
    {
        if (board.IsWhiteToMove) { if (move.TargetSquare.Rank == 7) eval += 40; }
        else if (move.TargetSquare.Rank == 2) eval += 40;
        if (move.TargetSquare.File == 3 || move.TargetSquare.File == 4) eval += 30;
        if (piecesLeftnow > 28) eval -= 30;
        eval -= 20;
    }
    if (move.MovePieceType == PieceType.Queen)// Queen piece square table + transition to mid/endgame
    {
        if (piecesLeftnow < 14) eval += 25;
        else eval -= 90;
        if (move.TargetSquare.File == 2 || move.TargetSquare.File == 3 || move.TargetSquare.File == 4 || move.TargetSquare.File == 5)
        {
            if (move.TargetSquare.Rank == 2 || move.TargetSquare.Rank == 3 || move.TargetSquare.Rank == 4 || move.TargetSquare.Rank == 5) eval += 45;
        }
    }

    if (move.MovePieceType == PieceType.Pawn)// Pawn "piece square table" This is mainly for early game
    {
        if(movesSinceLastPawnMove >= 25) eval += 25;
        if (piecesLeftnow < 14 || piecesLeftnow > 28) eval += 10;
        if (move.TargetSquare.File == 4 || move.TargetSquare.File == 5) eval += 30;
        eval += 5;
        if (piecesLeftnow < 8) eval += 70;
    }

    if(move.IsCastles) eval += 50; // castling is encouraged

    // We're out of the piece square tables!
    // This is for the flags to buff certain moves and nerf others
    // e.g. Checkmate is the highest priority move tied with en passant
    // Drawing is discouraged massively.
    // Checks are encouraged.
    // Moving away a piece that is attack is encouraged heavily.
    // Promotions are worth sacrificing a rook

    if (moveIsCheckmate(board, move)) eval = 999999999;
    if (moveHasBeenPlayed(board, move)) eval -= 1000;
    if (moveIsCheck(board, move)) eval += 20;
    if (board.SquareIsAttackedByOpponent(move.StartSquare)) eval += 120;
    if(move.IsPromotion) eval += 600;

    int currentDist = kingDSTfromOpponentKing(board);
    board.MakeMove(move);
    int newDist = kingDSTfromOpponentKing(board);
    board.UndoMove(move);
    if (piecesLeftnow < 6)
    {            
        if (newDist < currentDist) eval += 99;
    } else if (piecesLeftnow < 10) eval += 55;
    foreach (Move move2 in GetEnemyMoves(board))
    {
        if (moveIsCheckmate(board, move2)) eval -= 10000000;
        if (moveIsCheck(board, move2)) eval -= 60;
        if (moveHasBeenPlayed(board, move2)) eval -= 50000;
        if (move2.IsCapture) eval -= pieceVals[(int)move2.CapturePieceType];
        if (GetEnemyMoves(board).Length == 1) eval += 80;
    }

    return eval;

    
}



public Move Think(Board board, Timer timer)
{
    Move[] moves = board.GetLegalMoves();
    Move moveToPlay = moves[0];
    int bestEvaluation = -999999;
    foreach (Move move in moves)
    {
        if (evaluateMove(move, board) > bestEvaluation)
        {
            bestEvaluation = evaluateMove(move, board);
            moveToPlay = move;
        }
    }
    if (moveToPlay.MovePieceType == PieceType.Pawn) movesSinceLastPawnMove = 0;
    else movesSinceLastPawnMove++;
    return moveToPlay;
}
}






































































using System;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    //Adjusts accuracy of bot
    private int searchDepth = 4;

    //Diagnostic
    private int nodesearches = 0;

    //Stores the values of Piecetypes in a dictionary
    static readonly Dictionary<PieceType, int> pieceValues = new()
    {
            { PieceType.None, 0 },
            { PieceType.Rook, 50 },
            { PieceType.Bishop, 30 },
            { PieceType.Knight, 30 },
            { PieceType.Queen, 100 },
            { PieceType.Pawn, 10 },
            { PieceType.King, 10000 }
        };

    public Move Think(Board board, Timer timer)
    {
        //Diagnostic
        nodesearches = 0;

        //Store all legal moves in Array
        Move[] legalMoves = board.GetLegalMoves();

        //In case a better move isn't found, play a random move
        Random rand = new Random();
        Move bestMove = board.GetLegalMoves()[rand.Next(board.GetLegalMoves().Length)];

        //Chooses the best move out of all legal moves
        int bestEvaluation = int.MinValue;
        int evaluation;
        foreach (Move move in legalMoves)
        {
            if (move.IsPromotion && move.PromotionPieceType == PieceType.Queen)
            {
                return move;
            }

            board.MakeMove(move);
            int minimax = Minimax(board, searchDepth - 1, int.MinValue, int.MaxValue, false, timer);

            //Adjusts evaluation depending on if the bot has the white or black pieces
            evaluation = minimax * (board.IsWhiteToMove ? -1 : 1);
            board.UndoMove(move);

            //After the adjustement, a higher evaluation is favourable regardless of the pieces the bot plays
            if (evaluation > bestEvaluation)
            {
                bestEvaluation = evaluation;
                bestMove = move;
            }
        }

        //Diagnostic
        Console.WriteLine(nodesearches + " " + bestMove.ToString() + " " + bestEvaluation + " " + board.GetLegalMoves().Length);

        return bestMove;
    }

    public int Minimax(Board board, int depth, int alpha, int beta, bool isMaximizingPlayer, Timer timer)
    {
        //Ending clause of Algorithm
        if (depth == 0)
        {
            return Evaluate(board);
        }

        if (board.IsDraw())
        {
            return 0;
        }

        //Moves that result in Checkmates are the most important
        if (board.IsInCheckmate())
        {
            return isMaximizingPlayer ? int.MinValue : int.MaxValue;
        }

        //Looks into all possible evaluation outcomes
        Move[] legalMoves = board.GetLegalMoves();

        if (isMaximizingPlayer)
        {
            int maxEvaluation = int.MinValue;
            foreach (Move move in legalMoves)
            {
                board.MakeMove(move);

                //Recursively calls the method
                int evaluation = Minimax(board, depth - 1, alpha, beta, false, timer);

                //Diagnostic
                nodesearches++;

                board.UndoMove(move);
                maxEvaluation = Math.Max(maxEvaluation, evaluation);
                alpha = Math.Max(alpha, evaluation);

                //If a better move was found before, this one should not be considered
                if (beta <= alpha)
                {
                    break; // Beta cutoff
                }
            }
            return maxEvaluation;
        }
        else
        {
            int minEvaluation = int.MaxValue;
            foreach (Move move in legalMoves)
            {
                board.MakeMove(move);

                //Recursively calls the method
                int evaluation = Minimax(board, depth - 1, alpha, beta, true, timer);

                //Diagnostic
                nodesearches++;

                board.UndoMove(move);
                minEvaluation = Math.Min(minEvaluation, evaluation);

                //If a better move was found before, this one should not be considered
                beta = Math.Min(beta, evaluation);
                if (beta <= alpha)
                {
                    break; // Alpha cutoff
                }
            }
            return minEvaluation;
        }
    }
    public static int Evaluate(Board board)
    {
        //Evaluates the current state of the board based on how many pieces of each piecetype the players have
        int evaluation = 0;
        foreach (PieceType piecetype in Enum.GetValues(typeof(PieceType)))
        {
            int whitePieceCount = board.GetPieceList(piecetype, true)?.Count ?? 0;
            int blackPieceCount = board.GetPieceList(piecetype, false)?.Count ?? 0;

            // For white pieces, add their value to the evaluation
            if (piecetype != PieceType.None)
            {
                evaluation += pieceValues[piecetype] * whitePieceCount;
            }

            // For black pieces, subtract their value from the evaluation
            if (piecetype != PieceType.None)
            {
                evaluation -= pieceValues[piecetype] * blackPieceCount;
            }
        }

        //A positive evaluation is favourable for white, whereas a negative evaluation is favourable for black
        return evaluation;
    }

}





































// every 32 bits is a row. every 64-bit int here is 2 rows
static ulong[] piecePositionValueTable = {
    0x00000000050A0AEC, 0x05FBF60000000014, 0x05050A190A0A141E, 0x3232323200000000, // pawns
    0xCED8E2E2D8EC0005, 0xE2050A0FE2000F14, 0xE2050F14E2000A0F, 0xD8EC0000CED8E2E2, // knights
    0xECF6F6F6F6050000, 0xF60A0A0AF6000A0A, 0xF605050AF600050A, 0xF6000000ECF6F6F6, // bishops
    0x00000005FB000000, 0xFB000000FB000000, 0xFB000000FB000000, 0x050A0A0A00000000, // rooks
    0xECF6F6FBF6000000, 0xF605050500000505, 0xFB000505F6000505, 0xF6000000ECF6F6FB, // queens
    0x141E0A0014140000, 0xF6ECECECECE2E2D8, 0xE2D8D8CEE2D8D8CE, 0xE2D8D8CEE2D8D8CE  // kings
};

int GetPositionScore(int pieceType, int index) =>
    (sbyte)((piecePositionValueTable[pieceType * 4 + index / 16] >> (8 * (7 - (index % 8 < 4 ? index % 8 : 7 - index % 8) + index % 16 / 8 * 4))) & 0xFF);
