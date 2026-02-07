using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace EtherCAT_Studio
{
    public class OllamaClient
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private const string DefaultModel = "gemma2:2b";

        public OllamaClient(string baseUrl = "http://localhost:11434")
        {
            _baseUrl = NormalizeBaseUrl(baseUrl);
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };  // 복잡한 요청을 위해 120초로 증가
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            var url = (baseUrl ?? string.Empty).Trim();
            if (url.EndsWith("/"))
            {
                url = url.TrimEnd('/');
            }

            if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - 4);
            }

            return string.IsNullOrWhiteSpace(url) ? "http://localhost:11434" : url;
        }

        public async Task<string> GenerateAsync(string prompt)
        {
            try
            {
                var request = new
                {
                    model = DefaultModel,
                    prompt = prompt,
                    stream = false
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _client.PostAsync($"{_baseUrl}/api/generate", content);
                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(responseText);
                return doc.RootElement.GetProperty("response").GetString() ?? "";
            }
            catch (Exception ex)
            {
                throw new Exception($"Ollama API 호출 실패: {ex.Message}");
            }
        }
    }

    public class SequenceJsonGenerator
    {
        private readonly OllamaClient _ollama;

        public SequenceJsonGenerator(string? baseUrl = null)
        {
            _ollama = new OllamaClient(baseUrl ?? "http://localhost:11434");
        }

        public async Task<string> GenerateSequenceJsonAsync(string naturalLanguagePrompt)
        {
            var systemPrompt = @"JSON 생성 AI입니다. 사용자 요청을 정확한 JSON으로만 변환하세요.

=== 절대좌표(ABS MOVE) vs 상대좌표(REL MOVE) 구분 ===
절대좌표 (ABS MOVE): 특정 좌표값으로 이동
- 표현: ""X를 1000으로 이동"", ""X축 1000"", ""Y좌표 500로""
- 특징: 목표값, ""~로"", ""~까지"" 표현 사용
- 필드: axis, pos(목표값), speed

상대좌표 (REL MOVE): 현재위치에서 상대값만큼 이동  
- 표현: ""X축으로 500 이동"", ""Y축 -300"", ""Z에서 100 이동""
- 특징: 거리/변위값, ""~이동"", ""~만큼"" 표현 사용
- 필드: axis, distance(상대값), speed

판단 규칙:
1. ""~를/을 [숫자]로 이동"" → ABS MOVE (목표값)
2. ""~를/을 [숫자] 이동"" → REL MOVE (상대값)
3. ""[숫자]펄스"" 표현이 있고 ""로""가 없으면 → REL MOVE
4. ""좌표값"" 언급 → ABS MOVE
5. ""상대이동"", ""오프셋"" 언급 → REL MOVE

예시 분석:
✓ ""X축을 1000펄스 500속도로 이동"" → ABS MOVE (목표좌표 1000)
✓ ""X축으로 500 이동"" → REL MOVE (상대값 500)
✓ ""Y축 -300 이동"" → REL MOVE (음수도 상대값)
✓ ""X1000 Y500 이동"" → LINEAR_MOVE (3축 동시)
✓ ""X축을 2000으로"" → ABS MOVE (목표값 2000)

따옴표와 콜론 정확히 맞춰야 합니다.

예시 1) X축을 1000펄스 500속도로 이동 (절대좌표)
JSON:
{""sequence_name"":""X축 이동"",""steps"":[{""id"":""step_0"",""type"":""START"",""params"":{}},{""id"":""step_1"",""type"":""ABS MOVE"",""params"":{""axis"":""X"",""pos"":1000,""speed"":500}},{""id"":""step_2"",""type"":""END"",""params"":{}}]}

예시 2) X축으로 500 이동 후 2초 대기 (상대좌표)
JSON:
{""sequence_name"":""상대이동"",""steps"":[{""id"":""step_0"",""type"":""START"",""params"":{}},{""id"":""step_1"",""type"":""REL MOVE"",""params"":{""axis"":""X"",""distance"":500,""speed"":300}},{""id"":""step_2"",""type"":""WAIT"",""params"":{""delay"":2000}},{""id"":""step_3"",""type"":""END"",""params"":{}}]}

예시 3) 10번 카운터해서 반복 (카운터)
JSON:
{""sequence_name"":""10번 반복"",""steps"":[{""id"":""step_0"",""type"":""START"",""params"":{}},{""id"":""step_1"",""type"":""ABS MOVE"",""params"":{""axis"":""X"",""pos"":1000,""speed"":300}},{""id"":""step_2"",""type"":""COUNTER"",""params"":{""name"":""cnt1"",""initial"":0,""target"":10,""increment"":1,""gotoNode"":""node_01""}},{""id"":""step_3"",""type"":""END"",""params"":{}}]}

규칙:
- 항상 START로 시작, 항상 END로 끝남
- 각 필드: ""필드명"":값
- 문자열: ""값"" (따옴표 필수)
- 숫자: 1000, 500 (따옴표 없음)
- 쉼표로 구분, {} [] 정확히

타입 목록:
START = 시작
END = 종료
ABS MOVE = 절대좌표 이동 (목표값으로 이동)
REL MOVE = 상대좌표 이동 (현재에서 상대값만큼 이동)
WAIT = 대기
LINEAR_MOVE = 3축 선형 이동
CIRCULAR_MOVE = 원형 이동
COUNTER = 카운터
GOTO = 점프

params 구조:
- ABS MOVE: {""axis"":""X"", ""pos"":1000, ""speed"":500}
- REL MOVE: {""axis"":""Y"", ""distance"":500, ""speed"":300}
- WAIT: {""delay"":2000}
- LINEAR_MOVE: {""target"":{""X"":100,""Y"":200,""Z"":0}, ""speed"":300}
- CIRCULAR_MOVE: {""pass"":{""X"":100,""Y"":200}, ""end"":{""X"":300,""Y"":400}, ""direction"":""CW"", ""speed"":300, ""plane"":""XY""}
- COUNTER: {""name"":""cnt1"", ""initial"":0, ""target"":10, ""increment"":1, ""gotoNode"":""node_02""}
- GOTO: {""targetNode"":""node_05""}
- START/END: {}

JSON만 출력하세요. 설명은 금지.
";

            var fullPrompt = systemPrompt + "\n요청: " + naturalLanguagePrompt;

            try
            {
                var response = await _ollama.GenerateAsync(fullPrompt);
                
                // JSON 추출 및 검증
                var jsonStr = ExtractAndValidateJson(response);
                
                if (string.IsNullOrEmpty(jsonStr))
                {
                    throw new Exception($"유효한 JSON을 생성하지 못했습니다.\n응답: {response.Substring(0, Math.Min(300, response.Length))}");
                }

                // JSON에 "next" 필드 추가 및 포맷팅
                var formattedJson = NormalizeSequenceJson(jsonStr);
                return formattedJson;
            }
            catch (Exception ex)
            {
                throw new Exception($"시퀀스 생성 실패: {ex.Message}");
            }
        }

        private string NormalizeSequenceJson(string jsonStr)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                // 새로운 JSON 객체 생성
                var result = new Dictionary<string, object>();
                
                // sequence_name 복사
                if (root.TryGetProperty("sequence_name", out var seqName))
                {
                    result["sequence_name"] = seqName.GetString() ?? "Sequence";
                }
                else
                {
                    result["sequence_name"] = "Sequence";
                }

                // steps 배열 처리: next 필드 추가
                if (root.TryGetProperty("steps", out var stepsEl))
                {
                    var stepsArray = stepsEl.EnumerateArray().ToList();
                    var stepsWithNext = new List<Dictionary<string, object>>();

                    for (int i = 0; i < stepsArray.Count; i++)
                    {
                        var step = stepsArray[i];
                        var stepDict = new Dictionary<string, object>();

                        // id 처리: step_X를 node_XX로 변환
                        string nodeId = "node_01";
                        if (step.TryGetProperty("id", out var id))
                        {
                            string origId = id.GetString() ?? $"step_{i}";
                            // step_0 -> node_01, step_1 -> node_02 형식으로 변환
                            if (origId.StartsWith("step_") && int.TryParse(origId.Substring(5), out int stepNum))
                            {
                                nodeId = $"node_{(stepNum + 1):00}";
                            }
                            else
                            {
                                nodeId = origId;
                            }
                        }
                        stepDict["id"] = nodeId;

                        // type 처리: "REL MOVE" -> "REL_MOVE" 등 (ABS MOVE는 그대로 유지)
                        string nodeType = "START";
                        if (step.TryGetProperty("type", out var type))
                        {
                            string origType = type.GetString() ?? "START";
                            nodeType = origType switch
                            {
                                "ABS MOVE" => "ABS MOVE",  // ABS MOVE는 그대로 유지
                                "REL MOVE" => "REL_MOVE",
                                "LINEAR MOVE" => "LINEAR_MOVE",
                                "CIRCULAR MOVE" => "CIRCULAR_MOVE",
                                _ => origType.ToUpperInvariant()
                            };
                        }
                        stepDict["type"] = nodeType;

                        // params 처리: 필드명 정규화
                        if (step.TryGetProperty("params", out var paramsEl))
                        {
                            var normalizedParams = NormalizeParams(paramsEl, nodeType);
                            stepDict["params"] = normalizedParams;
                        }
                        else
                        {
                            stepDict["params"] = new Dictionary<string, object>();
                        }

                        // next 필드 추가: 마지막이 아니면 다음 id, 마지막이면 null
                        if (i < stepsArray.Count - 1)
                        {
                            var nextStep = stepsArray[i + 1];
                            string nextId = "node_02";
                            if (nextStep.TryGetProperty("id", out var nextIdEl))
                            {
                                string nextOrigId = nextIdEl.GetString() ?? $"step_{i + 1}";
                                if (nextOrigId.StartsWith("step_") && int.TryParse(nextOrigId.Substring(5), out int nextStepNum))
                                {
                                    nextId = $"node_{(nextStepNum + 1):00}";
                                }
                                else
                                {
                                    nextId = nextOrigId;
                                }
                            }
                            stepDict["next"] = nextId;
                        }
                        else
                        {
                            stepDict["next"] = null;
                        }

                        stepsWithNext.Add(stepDict);
                    }

                    result["steps"] = stepsWithNext;
                }

                // 포맷팅하여 반환
                var options = new JsonSerializerOptions { WriteIndented = true };
                return JsonSerializer.Serialize(result, options);
            }
            catch
            {
                // 실패시 원본 반환
                return jsonStr;
            }
        }

        private Dictionary<string, object> NormalizeParams(JsonElement paramsEl, string nodeType)
        {
            var result = new Dictionary<string, object>();

            foreach (var prop in paramsEl.EnumerateObject())
            {
                string key = prop.Name.ToLowerInvariant();
                var value = prop.Value;

                // 필드명 정규화
                string normalizedKey = key switch
                {
                    // ABS MOVE/REL_MOVE 관련
                    "pos" or "position" or "target_position" => "pos",
                    "axis" => "axis",
                    "speed" => "speed",
                    "distance" => "distance",
                    
                    // WAIT 관련
                    "delay" or "duration" => "delay_ms",
                    
                    // LINEAR_MOVE 관련
                    "target" => "target",
                    
                    // CIRCULAR_MOVE 관련
                    "pass" => "pass",
                    "end" => "end",
                    "direction" => "direction",
                    "plane" => "plane",
                    
                    // COUNTER 관련
                    "name" => "name",
                    "initial" or "initialvalue" => "initial",
                    "increment" or "step" => "increment",
                    "gotonode" => "gotoNode",
                    
                    // GOTO 관련
                    "to" => "to",
                    "description" => "description",
                    
                    // 기타
                    _ => key
                };

                // 값 처리
                object? normalizedValue = value.ValueKind switch
                {
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.String => value.GetString() ?? "",
                    JsonValueKind.Object => JsonDocument.Parse(value.GetRawText()).RootElement,
                    JsonValueKind.Array => JsonDocument.Parse(value.GetRawText()).RootElement,
                    JsonValueKind.Null => null,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => value.GetRawText()
                };

                if (!result.ContainsKey(normalizedKey) && normalizedValue != null)
                {
                    result[normalizedKey] = normalizedValue;
                }
            }

            return result;
        }

        private string? ExtractAndValidateJson(string response)
        {
            try
            {
                int jsonStart = response.IndexOf("{");
                if (jsonStart < 0)
                    return null;

                // 중괄호 균형을 맞춰서 JSON 끝 위치 찾기
                int braceCount = 0;
                int jsonEnd = -1;
                
                for (int i = jsonStart; i < response.Length; i++)
                {
                    if (response[i] == '{')
                        braceCount++;
                    else if (response[i] == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            jsonEnd = i;
                            break;
                        }
                    }
                }
                
                if (jsonEnd <= jsonStart)
                    return null;

                string jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                
                // JSON 유효성 검사
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                // 필수 필드 확인
                if (!root.TryGetProperty("steps", out var stepsEl) || stepsEl.ValueKind != JsonValueKind.Array)
                    return null;

                // steps 배열의 각 항목 검증
                int stepIndex = 0;
                foreach (var step in stepsEl.EnumerateArray())
                {
                    if (!step.TryGetProperty("id", out _) ||
                        !step.TryGetProperty("type", out _) ||
                        !step.TryGetProperty("params", out _))
                    {
                        return null;
                    }
                    stepIndex++;
                }

                return jsonStr;
            }
            catch (Exception ex)
            {
                // 디버그: 에러 로그
                System.Diagnostics.Debug.WriteLine($"ExtractAndValidateJson error: {ex.Message}");
                return null;
            }
        }
    }
}
