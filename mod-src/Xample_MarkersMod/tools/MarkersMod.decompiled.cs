using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using Utf8Json;
using Webserver;
using Webserver.WebAPI;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: AssemblyTitle("MarkersMod")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("The Fun Pimps LLC")]
[assembly: AssemblyProduct("")]
[assembly: AssemblyCopyright("The Fun Pimps LLC")]
[assembly: AssemblyTrademark("")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyVersion("0.0.0.0")]
namespace Examples;

public readonly struct MarkerData
{
	public readonly string Id;

	public readonly Vector2i Position;

	public readonly string Name;

	public readonly string Icon;

	public MarkerData(string _id, Vector2i _position, string _name, string _icon)
	{
		Id = _id;
		Position = _position;
		Name = _name;
		Icon = _icon;
	}
}
[UsedImplicitly]
public class ModApi : IModApi
{
	public void InitMod(Mod _modInstance)
	{
	}
}
[UsedImplicitly]
public class Markers : AbsRestApi
{
	private const int numRandomMarkers = 5;

	private const string defaultIcon = "https://upload.wikimedia.org/wikipedia/commons/thumb/1/11/Blue_question_mark_icon.svg/1200px-Blue_question_mark_icon.svg.png";

	private readonly Dictionary<string, MarkerData> markers = new Dictionary<string, MarkerData>();

	private static readonly byte[] jsonKeyId = JsonWriter.GetEncodedPropertyNameWithBeginObject("id");

	private static readonly byte[] jsonKeyX = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("x");

	private static readonly byte[] jsonKeyY = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("y");

	private static readonly byte[] jsonKeyName = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("name");

	private static readonly byte[] jsonKeyIcon = JsonWriter.GetEncodedPropertyNameWithPrefixValueSeparator("icon");

	public Markers()
	{
		GameRandom gameRandom = GameRandomManager.Instance.CreateGameRandom();
		for (int i = 0; i < 5; i++)
		{
			int x = gameRandom.RandomRange(-1000, 1001);
			int y = gameRandom.RandomRange(-1000, 1001);
			string text = WebUtils.GenerateGuid();
			markers.Add(text, new MarkerData(text, new Vector2i(x, y), "RandomMarker " + i, null));
		}
		GameRandomManager.Instance.FreeGameRandom(gameRandom);
	}

	public override void HandleRestGet(RequestContext _context)
	{
		string requestPath = _context.RequestPath;
		AbsRestApi.PrepareEnvelopedResult(out var _writer);
		MarkerData value;
		if (string.IsNullOrEmpty(requestPath))
		{
			_writer.WriteBeginArray();
			bool flag = true;
			foreach (var (_, marker) in markers)
			{
				if (!flag)
				{
					_writer.WriteValueSeparator();
				}
				flag = false;
				writeMarkerJson(ref _writer, marker);
			}
			_writer.WriteEndArray();
			AbsRestApi.SendEnvelopedResult(_context, ref _writer);
		}
		else if (!markers.TryGetValue(requestPath, out value))
		{
			_writer.WriteRaw(WebUtils.JsonEmptyData);
			AbsRestApi.SendEnvelopedResult(_context, ref _writer, HttpStatusCode.NotFound);
		}
		else
		{
			_writer.WriteBeginArray();
			writeMarkerJson(ref _writer, value);
			_writer.WriteEndArray();
			AbsRestApi.SendEnvelopedResult(_context, ref _writer);
		}
	}

	private void writeMarkerJson(ref JsonWriter _writer, MarkerData _marker)
	{
		_writer.WriteRaw(jsonKeyId);
		_writer.WriteString(_marker.Id);
		_writer.WriteRaw(jsonKeyX);
		_writer.WriteInt32(_marker.Position.x);
		_writer.WriteRaw(jsonKeyY);
		_writer.WriteInt32(_marker.Position.y);
		_writer.WriteRaw(jsonKeyName);
		_writer.WriteString(_marker.Name);
		_writer.WriteRaw(jsonKeyIcon);
		_writer.WriteString(_marker.Icon ?? "https://upload.wikimedia.org/wikipedia/commons/thumb/1/11/Blue_question_mark_icon.svg/1200px-Blue_question_mark_icon.svg.png");
		_writer.WriteEndObject();
	}

	public override void HandleRestPost(RequestContext _context, IDictionary<string, object> _jsonInput, byte[] _jsonInputData)
	{
		if (!JsonCommons.TryGetJsonField(_jsonInput, "x", out int _value))
		{
			AbsRestApi.SendEmptyResponse(_context, HttpStatusCode.BadRequest, _jsonInputData, "NO_OR_INVALID_X");
			return;
		}
		if (!JsonCommons.TryGetJsonField(_jsonInput, "y", out int _value2))
		{
			AbsRestApi.SendEmptyResponse(_context, HttpStatusCode.BadRequest, _jsonInputData, "NO_OR_INVALID_Y");
			return;
		}
		JsonCommons.TryGetJsonField(_jsonInput, "name", out string _value3);
		if (string.IsNullOrEmpty(_value3))
		{
			_value3 = null;
		}
		JsonCommons.TryGetJsonField(_jsonInput, "icon", out string _value4);
		if (string.IsNullOrEmpty(_value4))
		{
			_value4 = null;
		}
		string text = WebUtils.GenerateGuid();
		markers.Add(text, new MarkerData(text, new Vector2i(_value, _value2), _value3, _value4));
		AbsRestApi.PrepareEnvelopedResult(out var _writer);
		_writer.WriteString(text);
		AbsRestApi.SendEnvelopedResult(_context, ref _writer, HttpStatusCode.Created);
	}

	public override void HandleRestPut(RequestContext _context, IDictionary<string, object> _jsonInput, byte[] _jsonInputData)
	{
		if (!JsonCommons.TryGetJsonField(_jsonInput, "x", out int _value))
		{
			AbsRestApi.SendEmptyResponse(_context, HttpStatusCode.BadRequest, _jsonInputData, "NO_OR_INVALID_X");
			return;
		}
		if (!JsonCommons.TryGetJsonField(_jsonInput, "y", out int _value2))
		{
			AbsRestApi.SendEmptyResponse(_context, HttpStatusCode.BadRequest, _jsonInputData, "NO_OR_INVALID_Y");
			return;
		}
		string _value3;
		bool flag = !JsonCommons.TryGetJsonField(_jsonInput, "name", out _value3);
		string _value4;
		bool flag2 = !JsonCommons.TryGetJsonField(_jsonInput, "icon", out _value4);
		string requestPath = _context.RequestPath;
		if (!markers.TryGetValue(requestPath, out var value))
		{
			AbsRestApi.SendEmptyResponse(_context, HttpStatusCode.NotFound, _jsonInputData, "ID_NOT_FOUND");
			return;
		}
		if (flag)
		{
			_value3 = value.Name;
		}
		if (flag2)
		{
			_value4 = value.Icon;
		}
		MarkerData markerData = new MarkerData(requestPath, new Vector2i(_value, _value2), _value3, _value4);
		markers[requestPath] = markerData;
		AbsRestApi.PrepareEnvelopedResult(out var _writer);
		writeMarkerJson(ref _writer, markerData);
		AbsRestApi.SendEnvelopedResult(_context, ref _writer);
	}

	public override void HandleRestDelete(RequestContext _context)
	{
		string requestPath = _context.RequestPath;
		AbsRestApi.SendEmptyResponse(_context, markers.Remove(requestPath) ? HttpStatusCode.NoContent : HttpStatusCode.NotFound);
	}

	public override int[] DefaultMethodPermissionLevels()
	{
		return new int[5] { -2147483647, 2000, 1000, -2147483648, -2147483648 };
	}
}
