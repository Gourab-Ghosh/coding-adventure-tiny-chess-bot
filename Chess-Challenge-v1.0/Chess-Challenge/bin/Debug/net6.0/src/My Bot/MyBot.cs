using ChessChallenge.API;
using System;
using static System.Math;

public class MyBot : IChessBot
{
    static int timePerMove = 1000;
    static int nodes;
    static int[] pieceValues = {0, 100, 320, 330, 500, 900};
    static int[] positionValues = { 0, 5, 10, 20 };
    static Board board;
    static Timer timer;

    static int numKillerMoves = 5;
    static Move[] killerMoves;
    // static Move[] historyMoveScores;
    static Move bestMove;
    static ulong transpositionTableSize = 1000000;
    static (ulong, byte, short, byte, Move)[] transpositionTable = new (ulong, byte, short, byte, Move)[transpositionTableSize]; // (hash, depth, score, flag, bestMove)

    static int Popcount(ulong x)
    {
        return BitboardHelper.GetNumberOfSetBits(x);
    }

    static int ScoreMove(Move move, Move ttBestMove)
    {
        if (move.Equals(ttBestMove))
            return 110_000;
        if (move.Equals(bestMove))
            return 100_000;
        if (move.IsCapture)
            return 99_000 + 100 * (7 - (int) board.GetPiece(move.StartSquare).PieceType) + (int) board.GetPiece(move.TargetSquare).PieceType;
        for (int i = 0; i < numKillerMoves; i++)
            if (move.Equals(killerMoves[i]))
                return 98_000 - i;
        if (move.IsPromotion)
            return 97_000 - (int) move.PromotionPieceType;
        return 0;
    }

    static Move[] GetSortedLegalMoves(Move ttBestMove, bool capturesOnly = false)
    {
        Move[] moves = board.GetLegalMoves(capturesOnly);
        Array.Sort(moves, (a, b) => ScoreMove(b, ttBestMove) - ScoreMove(a, ttBestMove));
        return moves;
    }

    static int EvaluateBoard()
    {
        int score = 0;
        for (int i = 1; i < 6; i++)
        {
            score += pieceValues[i] * Popcount(board.GetPieceBitboard((PieceType) i, true));
            score -= pieceValues[i] * Popcount(board.GetPieceBitboard((PieceType) i, false));
        }
        ulong whitePiecesBitboard = board.WhitePiecesBitboard;
        ulong[] bitboards = {board.WhitePiecesBitboard, board.BlackPiecesBitboard};
        int position_score = 0;
        int sign = 1;
        foreach (ulong bitboard in bitboards) {
            ulong bb = bitboard;
            while (bb != 0)
            {
                int square = BitboardHelper.ClearAndGetIndexOfLSB(ref bb);
                int row = square / 8;
                int col = square % 8;
                position_score += sign * positionValues[Min(Min(row, 7-row), Min(col, 7-col))] * (6 - (int) board.GetPiece(new Square(square)).PieceType);
            }
            sign = -1;
        }
        score += position_score / 10;
        if (board.IsWhiteToMove)
            return score;
        return -score;
    }

    static void WriteTranspositionTable(byte depth, short score, byte flag, Move BestMove) {
        ulong index = board.ZobristKey % transpositionTableSize;
        (ulong, byte, short, byte, Move) ttEntry = transpositionTable[index];
        if (ttEntry.Item1 == board.ZobristKey && ttEntry.Item2 > depth)
            return;
        transpositionTable[index] = (board.ZobristKey, depth, score, flag, BestMove);
    }

    static int AlphaBeta(int depth, int ply = 0, int alpha = -30000, int beta = 30000)
    {
        if (board.IsDraw())
            return 0;
        int mate_score = 25000 - ply;
        // Mate distance pruning
        if (mate_score < beta)
        {
            beta = mate_score;
            if (alpha >= mate_score)
                return mate_score;
        }
        int score = -mate_score;
        if (board.IsInCheckmate())
            return score;
        bool isInCheck = board.IsInCheck();
        if (isInCheck)
            depth++;
        depth = Max(depth, 0);
        (ulong ttHash, byte ttDepth, short ttScore, byte ttFlag, Move ttBestMove) = transpositionTable[board.ZobristKey % transpositionTableSize];
        if (ply != 0 && ttHash == board.ZobristKey && ttDepth >= depth && Abs(ttScore) < 24000)
        {
            if (ttFlag == 0)
                return ttScore;
            if (ttFlag == 1 && ttScore <= alpha)
                return alpha;
            if (ttFlag == 2 && ttScore >= beta)
                return beta;
        }
        if (ttHash != board.ZobristKey)
            ttBestMove = Move.NullMove;
        if (depth == 0)
        {
            return Quiescence(ply, alpha, beta);
        }
        nodes++;
        // if (!isInCheck && ply != 0)
        // {
        //     if (depth >= 2)
        //     {
        //         int reduced_depth = (1728 * depth - 1920) / 4096;
        //         board.MakeMove(Move.NullMove);
        //         int null_move_score = -AlphaBeta(reduced_depth, ply + 1, -beta, -beta + 1);
        //         board.UndoMove(Move.NullMove);
        //         if (null_move_score >= beta)
        //             return beta;
        //     }
        // }
        Move[] moves = GetSortedLegalMoves(ttBestMove);
        byte flag = 1;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            score = -AlphaBeta(depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);
            if (timer.MillisecondsElapsedThisTurn > timePerMove)
                return 0;
            if (score > alpha)
            {
                alpha = score;
                flag = 0;
                if (ply == 0)
                    bestMove = move;
                if (alpha >= beta) {
                    if (!move.IsCapture)
                    {
                        WriteTranspositionTable((byte) depth, (short) beta, 2, move);
                        for (int i = numKillerMoves - 1; i > 0; i--)
                            killerMoves[i] = killerMoves[i-1];
                        killerMoves[0] = move;
                    }
                    return beta;
                }
            }
        }
        WriteTranspositionTable((byte) depth, (short) alpha, flag, ttBestMove);
        return alpha;
    }

    static int Quiescence(int ply, int alpha, int beta)
    {
        if (board.IsDraw())
            return 0;
        int score = EvaluateBoard();
        if (score >= beta)
            return beta;
        nodes++;
        if (score > alpha)
            alpha = score;
        foreach (Move move in GetSortedLegalMoves(Move.NullMove, true))
        {
            board.MakeMove(move);
            score = -Quiescence(ply + 1, -beta, -alpha);
            board.UndoMove(move);
            if (score >= beta)
                return beta;
            if (score > alpha)
                alpha = score;
        }
        return alpha;
    }

    public Move Think(Board board_, Timer timer_)
    {
        nodes = 0;
        board = board_;
        timer = timer_;
        bestMove = Move.NullMove;
        killerMoves = new Move[numKillerMoves];
        int depth = 1;
        while (timer.MillisecondsElapsedThisTurn < timePerMove)
        {
            if (depth > 150)
                break;
            int score = AlphaBeta(depth++);
            Console.Write(depth - 1);
            Console.Write(", ");
            Console.Write(nodes);
            Console.Write(", ");
            Console.WriteLine(score);
        }
        // Console.WriteLine(EvaluateBoard(board));
        // foreach (Move move in GetSortedLegalMoves(board))
        // {
        //     Console.Write(move);
        //     Console.Write(" ");
        // }
        return bestMove;
    }
}