#region License
// Derived from https://github.com/JamesNK/Newtonsoft.Json/blob/a3278ccd6a7ac88c3c5ae85d46a3ec46a6f438ec/Src/Newtonsoft.Json/Converters/IsoDateTimeConverter.cs
// which has the following license:
// Copyright (c) 2007 James Newton-King
// Copyright (c) 2015 Real Artists, Inc.
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RealArtists.GitHub {
  public class EpochDateTimeConverter : DateTimeConverterBase {
    private static readonly DateTime _Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset _EpochOffset = new DateTimeOffset(_Epoch);

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
      var nullable = IsNullableType(objectType);
      var t = nullable ? Nullable.GetUnderlyingType(objectType) : objectType;

      if (reader.TokenType == JsonToken.Null) {
        if (!nullable) {
          throw new JsonSerializationException(string.Format("Cannot convert null value to {0}.", objectType));
        }

        return null;
      }

      if (reader.TokenType == JsonToken.Date) {
        if (t == typeof(DateTimeOffset)) {
          return (reader.Value is DateTimeOffset) ? reader.Value : new DateTimeOffset((DateTime)reader.Value);
        }

        // converter is expected to return a DateTime
        if (reader.Value is DateTimeOffset) {
          return ((DateTimeOffset)reader.Value).DateTime;
        }

        return reader.Value;
      }

      long millisecs = 0;

      if (reader.TokenType == JsonToken.Integer) {
        millisecs = (long)reader.Value * 1000;
      } else if (reader.TokenType == JsonToken.Float) {
        millisecs = (long)((double)reader.Value * 1000);
      } else if (reader.TokenType == JsonToken.String) {
        millisecs = (long)(double.Parse((string)reader.Value) * 1000);
      } else {
        throw new JsonSerializationException(string.Format("Unexpected token parsing date. Expected numeric, got {0}.", reader.TokenType));
      }

      var date = _Epoch.AddMilliseconds(millisecs);

      if (t == typeof(DateTimeOffset)) {
        return new DateTimeOffset(date);
      } else {
        return date;
      }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
      double seconds = 0;

      if (value is DateTime) {
        var dateTime = (DateTime)value;
        dateTime = dateTime.ToUniversalTime();

        seconds = dateTime.Subtract(_Epoch).TotalSeconds;
      } else if (value is DateTimeOffset) {
        DateTimeOffset dateTimeOffset = (DateTimeOffset)value;
        dateTimeOffset = dateTimeOffset.ToUniversalTime();

        seconds = dateTimeOffset.Subtract(_EpochOffset).TotalSeconds;
      } else {
        throw new JsonSerializationException(string.Format("Unexpected value when converting date. Expected DateTime or DateTimeOffset, got {0}.", value.GetType()));
      }

      writer.WriteValue(seconds);
    }

    private static bool IsNullableType(Type t) {
      return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
    }
  }
}
