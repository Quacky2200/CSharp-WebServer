/**
 *    This program is free software: you can redistribute it and/or modify
 *    it under the terms of the GNU General Public License as published by
 *    the Free Software Foundation, either version 3 of the License, or
 *    (at your option) any later version.
 *
 *    This program is distributed in the hope that it will be useful,
 *    but WITHOUT ANY WARRANTY; without even the implied warranty of
 *    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *    GNU General Public License for more details.
 *
 *    You should have received a copy of the GNU General Public License
 *    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */
using System.Text;
using System.IO;

namespace WebServer.WebSockets
{
    public class MessageEventArgs
    {
        public MessageEventArgs(WebSocket.FrameOpCode OpCode, Stream Data)
        {
            switch (OpCode)
            {
                case WebSocket.FrameOpCode.TEXT_FRAME:
                    Type = "text";
                    break;
                case WebSocket.FrameOpCode.BINARY_FRAME:
                    Type = "binary";
                    break;
            }

            this.Data = Data;
        }

        public Stream Data { get; private set; }

        public string Type { get; private set; }

        public string GetString()
        {
            if (Type != "text") return null;
            Data.Position = 0;

            byte[] String = new byte[Data.Length];

            int Offset = 0;
            while (Offset < Data.Length)
            {
                if (Data.Length - Offset >= int.MaxValue)
                {
                    Offset += Data.Read(String, Offset, int.MaxValue);
                }
                else
                {
                    int Rest = (int)(Data.Length - Offset);
                    Offset += Data.Read(String, Offset, Rest);
                }
            }

            return Encoding.UTF8.GetString(String);
        }
    }
}
