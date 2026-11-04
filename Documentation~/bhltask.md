# BhlTask

C#-функции, забинденные как корутины BHL: вместо `Script_*` с `Init` / `Exec` / `Cleanup` на
каждый биндинг — одна статическая функция. Её ожидания идут в тиках того вызова BHL, который её
ждёт, и живут ровно столько же: проиграла ветка `paral`, остановлен fiber или VM — функция
отменена.

## Биндинг

В мете — корутина с прямым биндингом (`bit/metagen_bhl_bind`, `docs/bindings.md`): файл задаёт
`@bhl_bind_type:direct` и `@bhl_native_prefix` в `@shared_tokens`, `%` — имя функции:

```
@shared_tokens:{"bhl_bind":true, "bhl_bind_type":"direct", "bhl_native_prefix":"BitGames.BHL.Bind.ATFBind.%(..)"}

func ATF_WaitSeconds(
  @bhl_coroutine
  seconds : float
)
```

C#-функция возвращает одну из двух форм:

| Возвращает | Для чего | Отмена |
|---|---|---|
| `BhlTask`, `BhlTask<T>` | async-функция, по умолчанию | не возобновляется: без исключений и аллокаций, `finally` не выполняется |
| `BhlFinallyTask`, `BhlFinallyTask<T>` | async-функция с `finally` / `using` вокруг `await` | `OperationCanceledException`: `finally` выполняется, ~1 KB на функцию в цепочке |

```csharp
public static BhlTask ATF_WaitSeconds(float seconds) => BhlTask.WaitSeconds(seconds);

public static async BhlTask<bool> Tap(Vector3 pos)
{
  await BhlTask.Yield();
  return await ATFBot.Input.Tap(pos).ToBhlTask();
}
```

Возвращаемое значение биндинга — `BhlTask<T>` или `BhlFinallyTask<T>`.

## API

| | |
|---|---|
| `await BhlTask.Yield()` | следующий тик вызова, как `yield()` в BHL |
| `await BhlTask.WaitSeconds(s)` | масштабированное время (`Time.time`) |
| `task.Forget()` | задача идёт без ожидания; её исключение роняет вызов BHL |
| `uniTask.ToBhlTask()`, `task.ToBhlTask()` | ждать `UniTask` (если в проекте есть `com.cysharp.unitask`: define `BHL_UNITASK` из `versionDefines` asmdef) или `System.Threading.Tasks.Task` |
| `BhlTask.CompletedTask`, `new BhlTask<T>(v)`, `FromException(e)` | готовые задачи |

- **Ждать можно только свои awaiter.** Чужой (`UniTask`, `Task`) продолжил бы функцию вне тиков
  вызова, в том числе после его конца: такой `await` роняет задачу с `NotSupportedException`, а
  анализатор не даёт его скомпилировать (`BHLT001`). Чужую задачу переводит `ToBhlTask()`: она
  завершается в первый тик вызова после чужой.
- `Task` продолжается через `SynchronizationContext` главного потока, это кадр задержки и
  аллокации самого `Task`; `UniTask` — без них.
- **Задачу ждут один раз**, как в UniTask: источник уходит в пул, когда забран результат,
  повторный `await` бросает `InvalidOperationException` (анализатор: `BHLT004`).
- **Только главный поток**, как и VM.

## Время жизни и отмена

- Всё, что запустил вызов, умирает вместе с ним: когда корневая задача завершилась, недождавшиеся
  забытые задачи отменяются.
- **`BhlTask`** при отмене не возобновляется: продолжения отбрасываются, раннеры возвращаются в пул.
  Ни исключений, ни аллокаций, но и `finally` / `using` вокруг `await` не выполнятся — поэтому
  анализатор запрещает их в `BhlTask` (`BHLT002`).
- **`BhlFinallyTask`** возобновляется с `OperationCanceledException`, так что `finally` выполняется;
  `await` в его `finally` во время отмены сразу бросает то же исключение. Смешанные цепочки
  работают: `BhlTask`, который ждёт `BhlFinallyTask`, падает с исключением, сам не
  возобновляясь; `BhlFinallyTask` поверх `BhlTask` и поверх `ToBhlTask` получает исключение.
- `ToBhlTask` не останавливает чужую задачу — передай ей свой `CancellationToken`. Её результат
  забирается всё равно, когда она закончится.
- Исключение забытой задачи во время отмены тоже уходит в `Debug.LogException`;
  `OperationCanceledException` не логируется.

Отдельный тип, а не атрибут: в C# 9 метод не может выбрать себе builder (`[AsyncMethodBuilder]` на
методе — C# 10), а поиск атрибута рефлексией по state machine ненадёжен под IL2CPP stripping.

## Аллокации

На вызов, после того как пулы прогрелись (Release / Debug):

| | Release | Debug |
|---|---|---|
| `BhlTask` (старт, тики, завершение, `Forget`, `ToBhlTask(UniTask)`) | 0 | ~64 B на async-функцию |
| отмена `BhlTask` | 0 | 0 |
| отмена `BhlFinallyTask` | ~1 KB на функцию в цепочке | столько же |

- Debug: компилятор делает state machine классом. Редактор с Code Optimization = Debug
  аллоцирует, Release-плеер — нет.
- **Каждый лишний кадр стоит:** async-обёртка, которая только ждёт другую задачу, — это раннер,
  64 B в Debug и ещё одно исключение при отмене `BhlFinallyTask`. Пиши passthrough
  (`=> BhlTask.WaitSeconds(s)`); анализатор подсказывает (`BHLT005`).
- Аллоцируют ещё: исключения и `FromException`, `ToBhlTask(Task)`, первые вызовы, пока растут пулы,
  задача, которую не ждали и не забыли через `Forget`.

## Анализатор

Roslyn-анализатор `Runtime/Task/Analyzers/BitGames.BhlTask.Analyzer.dll` лежит в папке asmdef `BitGames.BhlTask`:
Unity применяет его к сборкам, которые ссылаются на `BitGames.BhlTask`.

| Код | Уровень | Когда |
|---|---|---|
| `BHLT001` | error | в `BhlTask` / `BhlFinallyTask`-функции ждут чужую задачу — нужен `ToBhlTask()` |
| `BHLT002` | error | в `BhlTask`-функции `await` внутри `try` с `finally`, `using` или после `using var` |
| `BHLT003` | error | результат `BhlTask` выброшен |
| `BHLT004` | warning | одну задачу ждут или забывают больше одного раза |
| `BHLT005` | warning | async-функция только ждёт задачу того же типа — лишний кадр |

Правила локальны для функции. Цепочку целиком анализировать не нужно: каждая функция цепочки
проверяется в своей сборке, а безопасность цепочки следует из безопасности звеньев (Roslyn и не
видит тел методов из других сборок). Не проверяются: `foreach` с `IDisposable`-перечислителем
внутри `BhlTask` (у обычных коллекций `Dispose` пустой) и повторный `await` в цикле.

### Сборка и тесты

Исходники — `Analyzer~/` (Unity не импортирует папки с `~`). Нужен .NET SDK 8.

```sh
cd <корень UnityBHL>
dotnet test "Analyzer~/BitGames.BhlTask.Analyzer.Tests"
# Release-сборка; после неё dll копируется в Runtime/Task/Analyzers/ (target CopyToUnity)
dotnet build -c Release "Analyzer~/BitGames.BhlTask.Analyzer"
```

Версия `Microsoft.CodeAnalysis.CSharp` (4.3.0) — та же, что у `com.bitgames.ref`: её поддерживает
Unity проекта.

## Тесты рантайма

`Tests~/BitGames.BhlTask.Tests` — консольная программа: рантайм BhlTask собирается обычным dotnet со
стабами Unity, VM и UniTask (`Stubs.cs`, `UniTaskStub.cs`). Вызов биндинга делается так же, как в
сгенерированном коде, тики и уничтожение — как в VM. Проверяются поведение (тики, результат, исключения,
отмена обеих форм, смешанные цепочки, `Forget`, `ToBhlTask`) и аллокации; в Release нулевые аллокации —
часть проверок. Код возврата — число упавших проверок.

```sh
cd <корень UnityBHL>
dotnet run -c Release --project "Tests~/BitGames.BhlTask.Tests"
dotnet run -c Debug --project "Tests~/BitGames.BhlTask.Tests"
```

## Устройство

```
Runtime/Task/BhlTask.cs           BhlTask, BhlFinallyTask, их awaiter
Runtime/Task/BhlTaskSource.cs     пуловые источники: раннер state machine, наблюдатель Forget, builder'ы
Runtime/Task/BhlTaskScope.cs      очередь продолжений вызова и его пуловые объекты, отмена
Runtime/Task/BhlTaskForeign.cs    ToBhlTask для UniTask (если он есть в проекте) и Task
Runtime/Task/BhlTaskCoroutine.cs  корутина bhl для задачи
Runtime/Task/Analyzers/           dll анализатора
Analyzer~/                   исходники анализатора и тесты
Tests~/                      тесты рантайма
```
