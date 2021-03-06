﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Cauldron
{
	/// <summary>
	/// Basic processor that reads game update JSON objects from a stream and writes GameEvent JSON objects out
	/// </summary>
	public class Processor
	{
		/// <summary>
		/// Parser has state, so store one per game we're tracking
		/// </summary>
		Dictionary<string, GameEventParser> m_trackedGames;

		private readonly JsonSerializerOptions m_serializerOptions;

		/// <summary>
		/// Constructor
		/// </summary>
		public Processor()
		{
			// I like camel case for my C# properties, sue me
			m_serializerOptions = new JsonSerializerOptions();
			m_serializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

			m_trackedGames = new Dictionary<string, GameEventParser>();
		}

		private IEnumerable<GameEvent> ProcessUpdate(string obj)
		{
			Update update = null;
			try
			{
				update = JsonSerializer.Deserialize<Update>(obj, m_serializerOptions);
			}
			catch(System.Text.Json.JsonException ex)
			{
				Console.WriteLine(ex.Message);
				Console.WriteLine($"While processing: {obj}");
				yield break;
			}

			// Currently we only care about the 'schedule' field that has the game updates
			foreach (var game in update.Schedule)
			{
				// Add new games if needed
				if (!m_trackedGames.ContainsKey(game._id))
				{
					GameEventParser parser = new GameEventParser();
					parser.StartNewGame(game, update.clientMeta.timestamp);

					m_trackedGames[game._id] = parser;
				}
				else
				{
					// Update a current game
					GameEventParser parser = m_trackedGames[game._id];
					GameEvent latest = parser.ParseGameUpdate(game, update.clientMeta.timestamp);

					if (latest != null)
					{
						yield return latest;
					}
				}
			}
		}


		public IEnumerable<GameEvent> Process(StreamReader newlineDelimitedJson)
		{
			List<GameEvent> events = new List<GameEvent>();

			while (!newlineDelimitedJson.EndOfStream)
			{
				string obj = newlineDelimitedJson.ReadLine();

				IEnumerable<GameEvent> newEvents = ProcessUpdate(obj);
				events.AddRange(newEvents);
			}

			return events;
		}

		public IEnumerable<GameEvent> Process(string newlineDelimitedJson)
		{
			List<GameEvent> events = new List<GameEvent>();

			StringReader sr = new StringReader(newlineDelimitedJson);
			string line = sr.ReadLine();
			while (line != null)
			{
				IEnumerable<GameEvent> newEvents = ProcessUpdate(line);
				events.AddRange(newEvents);

				line = sr.ReadLine();
			}

			return events;
		}

		/// <summary>
		/// Process all the JSON objects on a given stream and write output to another stream
		/// </summary>
		/// <param name="newlineDelimitedJson">Incoming JSON objects, newline delimited, in blaseball game update format</param>
		/// <param name="outJson">SIBR Game Event schema JSON objects, newline delimited</param>
		public void Process(StreamReader newlineDelimitedJson, StreamWriter outJson)
		{
			int linesRead = 0;
			while (!newlineDelimitedJson.EndOfStream)
			{
				string obj = newlineDelimitedJson.ReadLine();
				linesRead++;
				IEnumerable<GameEvent> newEvents = ProcessUpdate(obj);

				foreach(var e in newEvents)
				{
					// Write out the latest game event
					outJson.WriteLine(JsonSerializer.Serialize(e));
				}
			}

			int discards = m_trackedGames.Values.Sum(x => x.Discards);
			int processed = m_trackedGames.Values.Sum(x => x.Processed);
			int errors = m_trackedGames.Values.Sum(x => x.Errors);
			IEnumerable<string> errorGameIds = m_trackedGames.Values.Where(x => x.Errors > 0).Select(x => x.GameId);
			Console.WriteLine($"Error Games:");
			foreach(var game in errorGameIds)
				Console.WriteLine(game);
			Console.WriteLine("=========");
			Console.WriteLine($"Lines Read: {linesRead}\nUpdates Processed: {processed}\nDuplicates Discarded: {discards}\nGames With Errors: {errorGameIds.Count()}\nErrors: {errors}\nGames Found: {m_trackedGames.Keys.Count}");

		}
	}
}
