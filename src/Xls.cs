using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PositionLab {
static class Xls {
    internal static List<string[]> Read(string path) {
        byte[] file = File.ReadAllBytes(path);
        byte[] stream = Cfb.Stream(file, "Workbook");
        if (stream == null) stream = Cfb.Stream(file, "Book");
        if (stream == null) throw new FormatException("不是有效的 Excel 97-2003 文件（缺少 Workbook 流）。");
        return Records(stream);
    }
    static List<string[]> Records(byte[] wb) {
        var shared = new List<string>();
        var cells = new SortedDictionary<int,SortedDictionary<int,string>>();
        int pos = 0;
        while (pos + 4 <= wb.Length) {
            ushort id = U16(wb, pos), length = U16(wb, pos + 2);
            int data = pos + 4, next = data + length;
            if (next > wb.Length) break;
            switch (id) {
                case 0x00FC: {
                    var segments = new List<byte[]>();
                    segments.Add(Slice(wb, data, length));
                    int end = next;
                    while (end + 4 <= wb.Length && U16(wb, end) == 0x003C) {
                        int more = U16(wb, end + 2);
                        segments.Add(Slice(wb, end + 4, more));
                        end += 4 + more;
                    }
                    ParseSst(segments, shared);
                    pos = end;
                    continue;
                }
                case 0x00FD: {
                    int row = U16(wb, data), col = U16(wb, data + 2), index = (int)U32(wb, data + 6);
                    Cell(cells, row, col, index >= 0 && index < shared.Count ? shared[index] : "");
                    break;
                }
                case 0x0203:
                    Cell(cells, U16(wb, data), U16(wb, data + 2), BitConverter.ToDouble(wb, data + 6).ToString("R", Data.Inv));
                    break;
                case 0x027E:
                    Cell(cells, U16(wb, data), U16(wb, data + 2), Rk(U32(wb, data + 6)).ToString("R", Data.Inv));
                    break;
                case 0x00BD: {
                    int row = U16(wb, data);
                    int first = U16(wb, data + 2);
                    int count = (length - 6) / 6;
                    for (int k = 0; k < count; k++)
                        Cell(cells, row, first + k, Rk(U32(wb, data + 4 + k * 6 + 2)).ToString("R", Data.Inv));
                    break;
                }
                case 0x0006: {
                    int row = U16(wb, data), col = U16(wb, data + 2);
                    if (U16(wb, data + 12) != 0xFFFF)
                        Cell(cells, row, col, BitConverter.ToDouble(wb, data + 6).ToString("R", Data.Inv));
                    break;
                }
            }
            pos = next;
        }
        int width = 0;
        foreach (var row in cells.Values) foreach (int col in row.Keys) if (col + 1 > width) width = col + 1;
        int height = 0;
        foreach (int row in cells.Keys) if (row + 1 > height) height = row + 1;
        var grid = new List<string[]>();
        for (int r = 0; r < height; r++) {
            SortedDictionary<int,string> row;
            var cellsRow = new string[width];
            if (cells.TryGetValue(r, out row)) for (int c = 0; c < width; c++) cellsRow[c] = row.ContainsKey(c) ? row[c] : "";
            else for (int c = 0; c < width; c++) cellsRow[c] = "";
            grid.Add(cellsRow);
        }
        return grid;
    }
    static void Cell(SortedDictionary<int,SortedDictionary<int,string>> cells,int row,int col,string value) {
        SortedDictionary<int,string> line;
        if (!cells.TryGetValue(row, out line)) cells[row] = line = new SortedDictionary<int,string>();
        line[col] = value;
    }
    static byte[] Slice(byte[] data,int offset,int length) {
        if (offset < 0 || length <= 0 || offset + length > data.Length) return new byte[0];
        var copy = new byte[length];
        Array.Copy(data, offset, copy, 0, length);
        return copy;
    }
    // BIFF8 SST: a single string may straddle the 8 KB record limit. Its character data resumes in the
    // CONTINUE payload, whose first byte is a fresh option byte that can switch compressed/wide encoding.
    // The string header, rich-text runs and phonetic data are never split at that boundary.
    static void ParseSst(List<byte[]> segments,List<string> shared) {
        if (segments.Count == 0 || segments[0].Length < 8) return;
        int segment = 0, pos = 8;
        var current = segments[0];
        Func<int> available = () => current.Length - pos;
        Action nextSegment = () => {
            segment++;
            current = segment < segments.Count ? segments[segment] : null;
            pos = 0;
        };
        while (current != null) {
            if (available() < 3) { nextSegment(); continue; }
            int chars = U16(current, pos); byte flags = current[pos + 2]; pos += 3;
            int runs = 0, ext = 0;
            if ((flags & 0x08) != 0) {
                if (available() < 2) { nextSegment(); break; }
                runs = U16(current, pos); pos += 2;
            }
            if ((flags & 0x04) != 0) {
                if (available() < 4) { nextSegment(); break; }
                ext = (int)U32(current, pos); pos += 4;
            }
            bool wide = (flags & 0x01) != 0;
            var text = new StringBuilder(chars);
            int remaining = chars;
            while (remaining > 0) {
                int room = wide ? available() / 2 : available();
                if (room <= 0) {
                    nextSegment();
                    if (current == null) break;
                    if (available() < 1) break;
                    wide = (current[pos] & 0x01) != 0; pos++;
                    continue;
                }
                int take = Math.Min(room, remaining);
                int bytes = wide ? take * 2 : take;
                text.Append(wide ? Encoding.Unicode.GetString(current, pos, bytes) : Encoding.GetEncoding(1252).GetString(current, pos, bytes));
                pos += bytes; remaining -= take;
            }
            shared.Add(text.ToString());
            int skip = runs * 4 + ext;
            while (skip > 0) {
                if (available() <= 0) {
                    nextSegment();
                    if (current == null) return;
                    continue;
                }
                int take = Math.Min(skip, available());
                pos += take; skip -= take;
            }
        }
    }
    static double Rk(uint rk) {
        double value;
        if ((rk & 2) != 0) value = (int)rk >> 2;
        else value = BitConverter.Int64BitsToDouble((long)((ulong)(rk & 0xFFFFFFFC) << 32));
        if ((rk & 1) != 0) value /= 100;
        return value;
    }
    static ushort U16(byte[] data,int offset) { return (ushort)(data[offset] | data[offset + 1] << 8); }
    static uint U32(byte[] data,int offset) { return (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24); }

    static class Cfb {
        internal static byte[] Stream(byte[] file,string name) {
            if (file.Length < 512 || U32(file, 0) != 0xE011CFD0) return null;
            int sectorSize = 1 << U16(file, 30);
            int miniSize = 1 << U16(file, 32);
            uint dirStart = U32(file, 48);
            int entriesPerSector = sectorSize / 4;
            var fat = new List<uint>();
            for (int i = 0; i < 109; i++) {
                uint sector = U32(file, 76 + i * 4);
                if (sector >= 0xFFFFFFFA) break;
                fat.AddRange(Sector(file, sector, sectorSize, entriesPerSector));
            }
            uint difat = U32(file, 68);
            int difatCount = (int)U32(file, 72);
            for (int i = 0; i < difatCount && difat < 0xFFFFFFFA; i++) {
                var block = Sector(file, difat, sectorSize, entriesPerSector);
                for (int k = 0; k < entriesPerSector - 1; k++) if (block[k] < 0xFFFFFFFA) fat.AddRange(Sector(file, block[k], sectorSize, entriesPerSector));
                difat = block[entriesPerSector - 1];
            }
            byte[] directory = Chain(file, fat, dirStart, sectorSize);
            for (int offset = 0; offset + 128 <= directory.Length; offset += 128) {
                int nameLength = U16(directory, offset + 64);
                if (nameLength < 2) continue;
                string entryName = Encoding.Unicode.GetString(directory, offset, nameLength - 2);
                byte type = directory[offset + 66];
                if (type != 2 || !string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase)) continue;
                uint start = U32(directory, offset + 116);
                uint size = U32(directory, offset + 120);
                if (size < U32(file, 56)) continue;
                var data = Chain(file, fat, start, sectorSize);
                var result = new byte[size];
                Array.Copy(data, result, (int)Math.Min(size, (uint)data.Length));
                return result;
            }
            return null;
        }
        static uint[] Sector(byte[] file,uint sector,int size,int count) {
            var values = new uint[count];
            long offset = (long)(sector + 1) * size;
            for (int i = 0; i < count; i++) {
                long at = offset + i * 4;
                values[i] = at + 4 <= file.Length ? (uint)(file[at] | file[at + 1] << 8 | file[at + 2] << 16 | file[at + 3] << 24) : 0xFFFFFFFE;
            }
            return values;
        }
        static byte[] Chain(byte[] file,List<uint> fat,uint start,int size) {
            var output = new MemoryStream();
            uint sector = start;
            int guard = 0;
            while (sector < 0xFFFFFFFA && guard++ < 100000) {
                long offset = (long)(sector + 1) * size;
                if (offset + size > file.Length) break;
                output.Write(file, (int)offset, size);
                if (sector >= fat.Count) break;
                sector = fat[(int)sector];
            }
            return output.ToArray();
        }
    }
}
}
