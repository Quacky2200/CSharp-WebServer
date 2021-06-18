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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using WebServer.HTTP;

namespace WebServer.Routing
{
    public class Router
    {
        private readonly List<RouterUri> Routes = new List<RouterUri>();

        public delegate void RouterMatchHandler(Client Sender, RouteEventArgs E);

        public Router(Server Server)
        {
            Server.RequestReceived += Server_RequestReceived;
        }

        private void Server_RequestReceived(Client Sender, Request Request)
        {
            foreach (RouterUri Route in Routes)
            {
                if (!String.IsNullOrEmpty(Route.Method) && Request.Method != Route.Method) continue;

                Dictionary<string, string> Groups = Route.Groups;

                MatchCollection Collection = Route.CompiledRegex.Matches(Request.Path);

                if (Collection.Count < 1) continue;

                Match Match = Collection[0];

                List<string> Keys = Groups.Keys.ToList();

                for (int Idx = 0; Idx < Match.Groups.Count - 1; Idx++)
                {
                    // Skip first match group as it contains everything matched
                    Groups[Keys[Idx]] = Match.Groups[Idx + 1].Value;
                }

                Route.Handler(Sender, new RouteEventArgs(Route, Groups, Request));
                break;
            }
        }

        public Router Get(string Uri, RouterMatchHandler Delegate)
        {
            Routes.Add(new RouterUri(Uri, "GET", Delegate));
            return this;
        }

        public Router Post(string Uri, RouterMatchHandler Delegate)
        {
            Routes.Add(new RouterUri(Uri, "POST", Delegate));
            return this;
        }

        public Router Resolve(string Uri, RouterMatchHandler Delegate)
        {
            Routes.Add(new RouterUri(Uri, null, Delegate));
            return this;
        }

        public Router UnResolve(string Uri)
        {
            foreach (RouterUri Item in Routes)
            {
                if (Item.Uri != Uri) continue;
                Routes.Remove(Item);
                break;
            }

            return this;
        }
    }
}

