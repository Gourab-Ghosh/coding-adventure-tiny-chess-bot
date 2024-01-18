using ChessChallenge.API;
using System;
using static System.Math;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class EvilBot : IChessBot
    {
        static int timePerMove, ply;
    static int numKillerMoves = 3; // #DEBUG
    static int nodes; // #DEBUG
    static ulong transpositionTableSize = 1000000; // #DEBUG
    // static ulong transpositionTableSize = 16777216; // 256 MB #DEBUG
    static int[] pieceValuesAndPositionIndices = {
        0, 100, 320, 330, 500, 900, 0, // Piece values (King is the most useless piece in chess so it's value is 0 :p)
        -50, -40, -30, -20, -15, -10, -5, 0, 5, 10, 15, 20, 25, 30, 40, 50, // Position values
    };
    static ulong[] positionValuesCompressedIndices = {
        // 0x7777777777777777, 0x7777777777777777, // Null piece position indices which all evaluates to 0 (Just to adjust the array index easily)
        // 0x7777ffff99bd889c, 0x777b865789937777, // Pawn position indices
        // 0x1221377279a28ab, 0x27ab289a13780122, // Knight position indices
        // 0x3555577757895889, 0x5799599958773555, // Bishop position indices
        // 0x7777899967776777, 0x6777677767777778, // Rook position indices
        // 0x3456477757886788, 0x6788578847773456, // Queen position indices
        // 0x2110211021102110, 0x32215333bb77bd97, // King opening position indices
        // 0x123235725bd25de, 0x25de25bd22770222, // King endgame position indices
        0x7777777777777777, 0x7777777777777777, // Null piece position indices which all evaluates to 0 (Just to adjust the array index easily)
        0x7777ffff99bd889c, 0x777b865789937777, // Pawn position indices
        0x1221377279a28ab, 0x27ab289a13780122, // Knight position indices
        0x3555577757895889, 0x5799599958773555, // Bishop position indices
        0x7777899967776777, 0x6777677767777778, // Rook position indices
        0x3456477757886788, 0x6788578847773456, // Queen position indices
        0x2110211021102110, 0x32215333bb77bd97, // King opening position indices
        0x112135715bd25de, 0x25de15bd12770112, // King endgame position indices
    };
    static Board board_;
    static Timer timer_;
    static Move[,] killerMoves;
    static Move bestMove;
    static (ulong, byte, short, byte, ushort)[] transpositionTable; // (hash, depth, score, flag, bestMoveRawValue)

    static int ScoreMove(Move move, ushort ttBestMoveRawValue)
    {
        if (ply == 0 && move == bestMove)
            return 110_000;
        if (move.RawValue == ttBestMoveRawValue)
            return 100_000;
        int pieceTypeTimesHundred = 100 * (int) move.MovePieceType;
        if (move.IsCapture)
            return 99_000 - pieceTypeTimesHundred + (int) move.CapturePieceType;
        for (int i = 0; i < numKillerMoves; i++)
            if (move == killerMoves[ply, i])
                return 98_000 - i;
        if (move.IsPromotion)
            return 97_000 + (int) move.PromotionPieceType;
        if (move.IsCastles)
            return 96_000;
        return Min(move.TargetSquare.File, 7 - move.TargetSquare.File) - pieceTypeTimesHundred;
    }

    static int GetPositionScore(int compressedDataOffset, int dataIndex)
    {
        return pieceValuesAndPositionIndices[(int) ((positionValuesCompressedIndices[compressedDataOffset] >> (int) ((15 - dataIndex) * 4)) & 0xF) + 7];
    }

    // static int Popcount(ulong x)
    // {
    //     return BitboardHelper.GetNumberOfSetBits(x);
    // }

    static int EvaluateBoard()
    {
        // Decrease the score by 5 for each piece in the backranks
        int score = 5 * (BitboardHelper.GetNumberOfSetBits(board_.BlackPiecesBitboard & 0xFF00_0000_0000_00FF) - BitboardHelper.GetNumberOfSetBits(board_.WhitePiecesBitboard & 0xFF00_0000_0000_00FF))
        , pieceScoreAbs = 0, positionScore = 0;
        ulong allPiecesBB = board_.AllPiecesBitboard;
        foreach (PieceList pieceList in board_.GetAllPieceLists())
            pieceScoreAbs += pieceList.Count * pieceValuesAndPositionIndices[(int) pieceList.TypeOfPieceInList];
        while (allPiecesBB != 0)
        {
            int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref allPiecesBB), sign = -1;
            Piece piece = board_.GetPiece(new Square(squareIndex));
            if (piece.IsWhite) // Flipping the square horizontally for white pieces as the array index is mirror to square index
            {
                squareIndex ^= 0x38; // Flip the square vertically
                sign = 1;
            }
            // Adding the piece value to the score
            score += sign * pieceValuesAndPositionIndices[(int) piece.PieceType];
            // Extracting the data from the compressed data
            // Square square = new Square(squareIndex); // #DEBUG
            // int dataIndex1 = Min(square.File, 7-square.File) + 4 * square.Rank; // #DEBUG
            // int dataIndex = Min(squareIndex % 8, 7-(squareIndex % 8)) + 4 * (squareIndex / 8);
            int file = squareIndex % 8;
            // int dataIndex = (squareIndex + Min(file, 14 - 3*file)) / 2;
            int dataIndex = (squareIndex - file) / 2 + Min(file, 7 - file);
            // if (dataIndex != dataIndex1) // #DEBUG
            //     Console.WriteLine("Error"); // #DEBUG
            int compressedDataOffset = 2 * (int) piece.PieceType + dataIndex / 16;
            dataIndex %= 16;
            int currPiecePositionScore = GetPositionScore(compressedDataOffset, dataIndex);
            if (piece.PieceType == PieceType.King)
                // Interpolating king position score between opening and endgame position scores
                currPiecePositionScore = (int) (pieceScoreAbs * currPiecePositionScore + (8000 - pieceScoreAbs) * GetPositionScore(compressedDataOffset + 2, dataIndex)) / 8000;
            positionScore += sign * currPiecePositionScore;
        }
        score += positionScore / 2;
        return board_.IsWhiteToMove ? score : -score;
    }

    static void WriteTranspositionTable(int depth, int score, byte flag, ushort bestMoveRawValue) {
        ulong index = board_.ZobristKey % transpositionTableSize;
        (ulong, byte, short, byte, ushort) ttEntry = transpositionTable[index];
        // If the value is from quiescence search or the depth is greater than the current depth or the score is a mate score (>= 24000) then don't overwrite the entry
        if (depth == 0 || Abs(ttEntry.Item3) >= 24000 || (ttEntry.Item1 == board_.ZobristKey && ttEntry.Item2 > depth))
            return;
        transpositionTable[index] = (board_.ZobristKey, (byte) depth, (short) score, flag, bestMoveRawValue);
    }

    static int AlphaBetaAndQuiescence(int depth, int alpha = -30000, int beta = 30000) // Merged Quiescence and AlphaBeta into single function (depth == 0 means Quiescence search)
    {
        int mate_score = 25000 - ply, score = -mate_score;
        if (board_.IsInCheck() && depth > 1)
            depth++;
        depth = Max(depth, 0);
        if (board_.IsDraw())
            return 0;
        // Mate distance pruning
        if (mate_score < beta)
        {
            beta = mate_score;
            if (alpha >= mate_score)
                return mate_score;
        }
        if (board_.IsInCheckmate() && depth != 0)
            return score;
        (ulong ttHash, byte ttDepth, short ttScore, byte ttFlag, ushort ttBestMoveRawValue) = transpositionTable[board_.ZobristKey % transpositionTableSize];
        if (ply != 0 && ttHash == board_.ZobristKey && ttDepth >= depth && depth != 0)
        {
            if (ttFlag == 0)
                return ttScore;
            if (ttFlag == 1 && ttScore <= alpha)
                return alpha;
            if (ttFlag == 2 && ttScore >= beta)
                return beta;
        }
        if (ttHash != board_.ZobristKey)
            ttBestMoveRawValue = Move.NullMove.RawValue;
        if (depth == 0)
        {
            score = EvaluateBoard();
            if (score >= beta)
                return beta;
            alpha = Max(alpha, score);
        }
        nodes++; // #DEBUG
        Move[] moves = board_.GetLegalMoves(depth == 0);
        Array.Sort(moves, (a, b) => ScoreMove(b, ttBestMoveRawValue) - ScoreMove(a, ttBestMoveRawValue));
        if (ply == 0)
            bestMove = moves[0];
        byte flag = 1;
        foreach (Move move in moves)
        {
            board_.MakeMove(move);
            ply++;
            score = -AlphaBetaAndQuiescence(depth - 1, -beta, -alpha);
            board_.UndoMove(move);
            ply--;
            if (timer_.MillisecondsElapsedThisTurn > timePerMove)
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
                        for (int i = numKillerMoves - 1; i > 0; i--)
                            killerMoves[ply, i] = killerMoves[ply, i-1];
                        killerMoves[ply, 0] = move;
                    }
                    WriteTranspositionTable(depth, beta, 2, move.RawValue);
                    return beta;
                }
            }
        }
        WriteTranspositionTable(depth, alpha, flag, ttBestMoveRawValue);
        return alpha;
    }

    public Move Think(Board board, Timer timer)
    {
        nodes = 0; // #DEBUG
        ply = 0;
        board_ = board;
        timer_ = timer;
        killerMoves = new Move[150, numKillerMoves]; // 150 is the maximum ply
        transpositionTable = new (ulong, byte, short, byte, ushort)[transpositionTableSize];
        // timePerMove = Max(10, (timer_.MillisecondsRemaining - Max(0, timer_.OpponentMillisecondsRemaining - timer_.MillisecondsRemaining)) / 30);
        // timePerMove = Max(10, timer_.MillisecondsRemaining / 30);
        timePerMove = 100;
        int depth = 1;
        Console.WriteLine($"Position: {board_.GetFenString()}"); // #DEBUG
        do
        { // #DEBUG
            int score = AlphaBetaAndQuiescence(depth++);
            Console.WriteLine($"Depth: {depth - 1}, Nodes: {nodes}, NPS: {(1000 * nodes) / Max(1, timer_.MillisecondsElapsedThisTurn)}, Score: {score}"); // #DEBUG
        } // #DEBUG
        while (timer_.MillisecondsElapsedThisTurn < timePerMove && depth <= 100); // 100 is the maximum depth
        return bestMove;
    }
    }
}