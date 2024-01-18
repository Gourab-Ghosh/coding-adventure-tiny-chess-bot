import os
import string
import numpy as np
from pprint import pprint
import itertools as it

# https://docs.google.com/forms/d/e/1FAIpQLSfOWUXshbWcf2jvOJAtCDwcWiBsqgCjNTZehRN-RJHBsmv3Kw/alreadyresponded

code = r"""

int timeDifference = Max(0, timer.OpponentMillisecondsRemaining - timer.MillisecondsRemaining);
if (timeDifference > 3600_000)
    timeDifference = 0;
int timePerMove = Max(
    10,
    Min(
        500 * board.PlyCount,
        (timer.MillisecondsRemaining - timeDifference) / 15 + timer.IncrementMilliseconds - 100
    )
)
, ply = 0
, depth = 1;
var killerMoves = new Move[150, 3];
int[] pieceValuesAndPositionIndices = {
    0, 100, 320, 330, 500, 900, 0,
    -50, -40, -30, -20, -15, -10, -5, 0, 5, 10, 15, 20, 25, 30, 40, 50,
};
ulong[] positionValuesCompressedIndices = {
    0x7777777777777777, 0x7777777777777777, 0x7777777777777777, 0x7777777777777777,
    0x7777FFFF9ABD889C, 0x777B865789937777, 0xFFFFEEEEDDDDBBBB, 0x9999888866667777,
    0x01221378289A27AB, 0x27AB289A13780122, 0x01221356259A26AB, 0x26AB259A13560122,
    0x3555577757895889, 0x5799599958773555, 0x355557775799579D, 0x579D579957773555,
    0x7777899967776777, 0x6777677767777778, 0xEEEEFFFFEEEEEEEE, 0xEEEEEEEEEEEEEEEE,
    0x3556577757886789, 0x7789588857873556, 0x3556577757886789, 0x6789578857773556,
    0x2110211021102110, 0x32215433BB77BD97, 0x0122135715BD25DE, 0x25DE15BD12770112,
};

int DecompressData(int compressedDataOffset, int dataIndex) =>
    pieceValuesAndPositionIndices[((positionValuesCompressedIndices[compressedDataOffset] >> (60 - 4 * dataIndex)) & 0xF) + 7];

int EvaluateBoard()
{
    int score = 0, pieceScoreAbs = 0;
    foreach (PieceList pieceList in board.GetAllPieceLists())
        pieceScoreAbs += pieceList.Count * pieceValuesAndPositionIndices[(int)pieceList.TypeOfPieceInList];
    ulong allPiecesBB = board.AllPiecesBitboard;
    while (allPiecesBB != 0)
    {
        int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref allPiecesBB)
        , pieceColorSign = -1;
        Square square = new(squareIndex);
        Piece piece = board.GetPiece(square);
        if (piece.IsWhite)
        {
            squareIndex ^= 0x38;
            pieceColorSign = 1;
        }
        score += pieceColorSign * pieceValuesAndPositionIndices[(int)piece.PieceType];
        int file = squareIndex % 8;
        int dataIndex = (squareIndex + Min(file, 14 - 3 * file)) / 2;
        int compressedDataOffset = 4 * (int)piece.PieceType + dataIndex / 16;
        dataIndex %= 16;
        int openingScore = DecompressData(compressedDataOffset, dataIndex);
        if (piece.IsKing && BitboardHelper.SquareIsSet(0xFFFF_0000_0000_FFFF, square)) {
            openingScore += 20 * BitboardHelper.GetNumberOfSetBits(
                BitboardHelper.GetKingAttacks(square) &
                board.GetPieceBitboard(PieceType.Pawn, piece.IsWhite)
            );
        }
        score += pieceColorSign * (
            pieceScoreAbs * openingScore +
            (8000 - pieceScoreAbs) * DecompressData(compressedDataOffset + 2, dataIndex)
        ) / 8000;
    }
    return board.IsWhiteToMove ? score : -score;
}

int ScoreMove(Move move, ushort ttBestMoveRawValue)
{
    if (ply == 0 && move == bestMove)
        return 99_000;
    if (move.RawValue == ttBestMoveRawValue)
        return 98_000;
    int pieceTypeTimesHundred = 100 * (int)move.MovePieceType;
    if (move.IsCapture)
        return 97_000 - pieceTypeTimesHundred + (int)move.CapturePieceType;
    for (int i = 0; i < 3; i++)
        if (move == killerMoves[ply, i])
            return 96_000 - i;
    if (move.IsPromotion)
        return 95_000 + (int)move.PromotionPieceType;
    if (move.IsCastles)
        return 94_000;
    return Min(move.TargetSquare.File, 7 - move.TargetSquare.File) - pieceTypeTimesHundred;
}

void WriteTranspositionTable(int depth, int score, byte flag, ushort bestMoveRawValue)
{
    ulong index = board.ZobristKey % 8388608;
    (ulong ttHash, byte ttDepth, short ttScore, _, _) = transpositionTable[index];
    if (depth != 0 && Abs(ttScore) <= 24000 && (ttHash != board.ZobristKey || ttDepth <= depth))
        transpositionTable[index] = (board.ZobristKey, (byte)depth, (short)score, flag, bestMoveRawValue);
}

int AlphaBetaAndQuiescence(int depth, int alpha = -30000, int beta = 30000)
{
    int mate_score = 25000 - ply, score = -mate_score;
    if (board.IsInCheck() && depth > 1)
        depth++;
    depth = Max(depth, 0);
    bool isNotQuiescenceSearch = depth != 0;
    if (board.IsInCheckmate() && isNotQuiescenceSearch)
        return score;
    if (board.IsDraw())
        return 0;
    alpha = Max(alpha, -mate_score);
    beta = Min(beta, mate_score - 1);
    if (alpha >= beta)
        return alpha;
    (ulong ttHash, byte ttDepth, short ttScore, byte ttFlag, ushort ttBestMoveRawValue) = transpositionTable[board.ZobristKey % 8388608];
    if (ply != 0 && ttHash == board.ZobristKey && ttDepth >= depth && isNotQuiescenceSearch)
    {
        if (ttFlag == 0)
            return ttScore;
        if (ttFlag == 1 && ttScore <= alpha)
            return alpha;
        if (ttFlag == 2 && ttScore >= beta)
            return beta;
    }
    if (ttHash != board.ZobristKey)
        ttBestMoveRawValue = Move.NullMove.RawValue;
    if (depth > 2 && Abs(beta) <= 24000)
        if (board.TrySkipTurn())
        {
            ply++;
            int nullMoveReductionScore = -AlphaBetaAndQuiescence(depth - 1 - Max(2, depth / 2), -beta, 1 - beta);
            board.UndoSkipTurn();
            ply--;
            if (nullMoveReductionScore >= beta)
                return beta;
        }
    if (!isNotQuiescenceSearch)
    {
        score = EvaluateBoard();
        if (score >= beta)
            return beta;
        alpha = Max(alpha, score);
    }
    var moves = board.GetLegalMoves(!isNotQuiescenceSearch);
    Array.Sort(moves, (a, b) => ScoreMove(b, ttBestMoveRawValue) - ScoreMove(a, ttBestMoveRawValue));
    if (ply == 0)
        bestMove = moves[0];
    byte flag = 1;
    foreach (Move move in moves)
    {
        board.MakeMove(move);
        ply++;
        score = -AlphaBetaAndQuiescence(depth - 1, -beta, -alpha);
        board.UndoMove(move);
        ply--;
        if (timer.MillisecondsElapsedThisTurn > timePerMove)
            return 0;
        if (score > alpha)
        {
            alpha = score;
            flag = 0;
            if (ply == 0)
                bestMove = move;
            if (alpha >= beta)
            {
                if (!move.IsCapture)
                {
                    for (int i = 2; i > 0; i--)
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

do
    AlphaBetaAndQuiescence(depth++);
while (timer.MillisecondsElapsedThisTurn < timePerMove && depth <= 100);

""".strip()

template = """

using ChessChallenge.API;
using System;
using static System.Math;

public class MyBot : IChessBot
{
    Move bestMove;
    readonly (ulong, byte, short, byte, ushort)[] transpositionTable = new (ulong, byte, short, byte, ushort)[8388608];

    public Move Think(Board board, Timer timer)
    {
        // String uniqueChars = \"{unique_chars}\";
        {compressed_code}
        return bestMove;
    }
}

""".strip()

def get_compressed_code(code):
    code = code.replace("\n", "")
    while "  " in code:
        code = code.replace("  ", " ")
    compressed_code = code[0]
    for i, char in enumerate(code[1:-1]):
        i += 1
        if char == " " and not (code[i-1] in string.ascii_letters and code[i+1] in string.ascii_letters + string.digits):
            continue
        if char == "_" and code[i-1] in string.ascii_letters + string.digits and code[i+1] in string.ascii_letters + string.digits:
            continue
        compressed_code += char
    compressed_code += code[-1]
    return compressed_code

def get_unique_tokens(code):
    token = ""
    unique_tokens = set()
    for char in code:
        if char in string.ascii_letters + string.digits:
            token += char
        else:
            if token != "":
                unique_tokens.add(token)
            token = ""
    return unique_tokens

def generate_whole_code(**kwargs):
    whole_code = template
    for key, value in kwargs.items():
        whole_code = whole_code.replace("{" + key + "}", value)
    return whole_code

def rename_variables(compressed_code):
    return compressed_code
    unchanged_tokens = set("Abs Array AllPiecesBitboard bestMove BitboardHelper CapturePieceType ClearAndGetIndexOfLSB Count File GetAllPieceLists GetKingAttacks GetLegalMoves GetNumberOfSetBits GetPiece GetPieceBitboard IncrementMilliseconds IsCapture IsCastles IsDraw IsInCheck IsInCheckmate IsKing IsPromotion IsWhite IsWhiteToMove MakeMove Max MillisecondsElapsedThisTurn MillisecondsRemaining Min Move MovePieceType NullMove OpponentMillisecondsRemaining Pawn Piece PieceList PieceType PlyCount PromotionPieceType RawValue Sort Square SquareIsSet TargetSquare TrySkipTurn TypeOfPieceInList UndoMove UndoSkipTurn ZobristKey board bool byte do for foreach if in int new ref return short timer ulong ushort var void while".split(" "))
    unique_tokens = get_unique_tokens(compressed_code)
    for token in unique_tokens.copy():
        if token.isdigit() or token.lower().startswith("0x") or len(token) < 2:
            unchanged_tokens.add(token)
            unique_tokens.remove(token)
    for token in unchanged_tokens:
        if token in unique_tokens:
            unique_tokens.remove(token)
    def generate_names():
        n = 1
        while True:
            for i in it.permutations(string.ascii_letters, n):
                name = "".join(i)
                if name in unchanged_tokens | unchanged_tokens:
                    continue
                yield name
            n += 1
    names = generate_names()
    for token in unique_tokens:
        compressed_code = compressed_code.replace(token, next(names))
    return compressed_code

compressed_code = get_compressed_code(code)
unique_chars = "".join(sorted(np.unique(list(compressed_code))))

whole_code = generate_whole_code(
    unique_chars = repr(unique_chars)[1:-1],
    compressed_code = rename_variables(compressed_code),
)
src_file_path = os.path.join("src", "My Bot", "MyBot.cs")

with open(src_file_path, "w") as wf:
    wf.write(whole_code)

# os.system("dotnet run --configuration Release")