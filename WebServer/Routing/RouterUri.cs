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
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WebServer.Routing
{
    public class RouterUri
    {
        public string Uri { get; }
        public string Method { get; }

        public string Pattern { get; }
        public Regex CompiledRegex { get; }

        public Dictionary<string, string> Groups { get; }
        public Router.RouterMatchHandler Handler { get; }

        public RouterUri(string Uri, string Method, Router.RouterMatchHandler Handler)
        {
            this.Uri = Uri;
            this.Method = Method;
            this.Handler = Handler;
            this.Groups = new Dictionary<string, string>();
            this.Pattern = this.GenerateRegex();
            this.CompiledRegex = new Regex(this.Pattern, RegexOptions.Compiled);
        }

        private string GenerateGroupRegex(Match Match)
        {
            string Optional = "";
            string Name = Match.Groups[2].Value; // Remove '/:'

            if (Name.Last() == '?')
            {
                Optional = "?";
                Name = Name.Substring(0, Name.Length - 1);
            }

            string GroupPattern = @"(?:\/([^\/]+))";

            // Allow Fixed Group Expressions (e.g. '/:user(bob|alice)')
            Match FixedGroupMatch = Regex.Match(Name, @"\([\w_\d\|]+\)");
            if (FixedGroupMatch.Success)
            {
                Name = Name.Replace(FixedGroupMatch.Value, "");
                // Brackets included in Match 0
                GroupPattern = $"(:\\/{FixedGroupMatch.Value})";
            }

            Groups.Add(Name, "");
            return GroupPattern + Optional;
        }

        private string GenerateRegex()
        {
            MatchEvaluator GenerateGroups = new MatchEvaluator(GenerateGroupRegex);
            return "^" + Regex.Replace(this.Uri, @"(\/:([^\/]+))", GenerateGroups) + @"(?:\/|$)";
        }
    }
}
