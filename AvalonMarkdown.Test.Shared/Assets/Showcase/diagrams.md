# Diagrams — showcase

Mermaid and PlantUML, every diagram type worth exercising. Kept as a real `.md`
asset so `render_check.py` validates the exact file the demo renders.

**Mermaid** renders in the page. A diagram it cannot parse gets an error diagram
injected into the DOM, but the diagram *source* is destroyed by that path — so if
one of these fails, the text below is the only copy.

**PlantUML** renders on `plantuml.com` and comes back as an image. A syntax error
returns HTTP 400 whose body is itself a *renderable* error picture, so a broken
diagram still displays at full size looking deliberate. Only reading the response
body — which `render_check.py --plantuml` does — tells the two apart.

---

## 1. Mermaid

### 1.1 Flowchart — shapes and edge styles

```mermaid
graph TD
    A[Rectangle] --> B(Rounded)
    B --> C{Diamond}
    C -->|yes| D([Stadium])
    C -->|no| E[[Subroutine]]
    D --> F[(Database)]
    E --> F
    F --> G((Circle))
```

### 1.2 Flowchart — subgraphs and directions

```mermaid
flowchart LR
    subgraph Client
        A[Browser] --> B[Cache]
    end
    subgraph Server
        C[API] --> D[(Store)]
    end
    B --> C
    D --> E[Replica]
```

### 1.3 Sequence diagram

```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant A as API
    participant D as Database
    U->>A: POST /login
    activate A
    A->>D: SELECT user
    D-->>A: row
    alt credentials valid
        A-->>U: 200 token
    else invalid
        A-->>U: 401
    end
    deactivate A
    Note over U,D: the whole exchange is logged
```

### 1.4 Class diagram

```mermaid
classDiagram
    class Shape {
        <<abstract>>
        +String name
        +area() double
    }
    class Circle {
        +double radius
        +area() double
    }
    class Square {
        +double side
    }
    Shape <|-- Circle
    Shape <|-- Square
    Circle *-- Point : centre
    Shape ..> Logger : uses
```

### 1.5 State diagram

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Loading : fetch
    Loading --> Ready : 200
    Loading --> Failed : 4xx
    Failed --> Loading : retry
    Ready --> [*]
```

### 1.6 Entity relationship

```mermaid
erDiagram
    CUSTOMER ||--o{ ORDER : places
    ORDER ||--|{ LINE_ITEM : contains
    CUSTOMER {
        string id PK
        string name
    }
    ORDER {
        string id PK
        date placed
    }
```

### 1.7 Gantt

```mermaid
gantt
    title Release plan
    dateFormat YYYY-MM-DD
    section Design
    Spec        :done,    s1, 2026-01-01, 10d
    Review      :active,  s2, after s1, 5d
    section Build
    Implement   :         s3, after s2, 20d
    Test        :         s4, after s3, 10d
```

### 1.8 Pie

```mermaid
pie showData
    title Requests by status
    "200" : 812
    "304" : 120
    "404" : 31
    "500" : 4
```

### 1.9 Git graph

```mermaid
gitGraph
    commit id: "init"
    branch develop
    commit id: "feature"
    checkout main
    merge develop
    commit id: "release"
```

### 1.10 Mindmap

```mermaid
mindmap
    root((renderer))
        Markdown
            headings
            tables
            footnotes
        Math
            KaTeX
        Diagrams
            Mermaid
            PlantUML
        Plots
            function-plot
```

### 1.11 Timeline

```mermaid
timeline
    title Release history
    8.0 : theme editor
    9.0 : rename to MarkdownThemeViewModel
    10.0 : function plots
    11.0 : image export
```

### 1.12 Quadrant chart

```mermaid
quadrantChart
    title Effort against value
    x-axis Low effort --> High effort
    y-axis Low value --> High value
    quadrant-1 Do now
    quadrant-2 Plan
    quadrant-3 Drop
    quadrant-4 Delegate
    Feature A: [0.3, 0.8]
    Feature B: [0.7, 0.7]
    Feature C: [0.2, 0.2]
```

### 1.13 XY chart

```mermaid
xychart-beta
    title "Latency over time"
    x-axis [1, 2, 3, 4, 5, 6]
    y-axis "ms" 0 --> 120
    line [20, 35, 30, 55, 48, 90]
    bar  [15, 25, 22, 40, 35, 60]
```

### 1.14 A diagram with CJK and punctuation in its labels

```mermaid
graph LR
    A[解析 Markdown] --> B{是否为图表？}
    B -->|是，mermaid| C[渲染 SVG]
    B -->|"否（普通代码）"| D[高亮并显示]
```

### 1.15 A diagram with HTML in its labels

Mermaid's `htmlLabels` option is on by default, so these labels become a
`<foreignObject>` — which the PNG export has to render. It does; that was
measured, not assumed.

```mermaid
graph TD
    A["<b>Bold</b> and <i>italic</i> label"] --> B["a<br/>line break"]
```

### 1.16 A deliberately broken diagram

Mermaid replaces this with its own error diagram, reading *Syntax error in text*.

```mermaid
graph TD
    A[Start --> B{Unclosed
```

### 1.17 A misspelled info string is not a diagram at all

`mermiad` matches nothing, so this renders as a plain highlighted code block —
silently, with no warning anywhere.

```mermiad
graph TD
    A --> B
```

---

## 2. PlantUML

### 2.1 Sequence

```plantuml
@startuml
actor User
participant "API" as A
database DB
User -> A: request
activate A
A -> DB: query
DB --> A: rows
deactivate A
A --> User: response
@enduml
```

### 2.2 Use case

```plantuml
@startuml
left to right direction
actor Customer
actor Admin
rectangle Shop {
  Customer --> (Browse)
  Customer --> (Checkout)
  Admin --> (Manage stock)
  (Checkout) .> (Browse) : include
}
@enduml
```

### 2.3 Class

```plantuml
@startuml
abstract class Shape {
  +String name
  +{abstract} double area()
}
class Circle {
  +double radius
  +double area()
}
class Square {
  +double side
}
Shape <|-- Circle
Shape <|-- Square
Shape o-- Point
@enduml
```

### 2.4 Activity, modern syntax

```plantuml
@startuml
start
:Read input;
if (valid?) then (yes)
  :Process;
  :Write result;
else (no)
  :Report error;
  stop
endif
:Done;
stop
@enduml
```

### 2.5 Component

```plantuml
@startuml
package "Client" {
  [Browser] as B
}
package "Server" {
  [API] as A
  [Worker] as W
  database "Postgres" as P
}
B --> A : HTTPS
A --> P : SQL
A --> W : enqueue
@enduml
```

### 2.6 State

```plantuml
@startuml
[*] --> Idle
Idle --> Loading : fetch
Loading --> Ready : 200
Loading --> Failed : 4xx
Failed --> Loading : retry
Ready --> [*]
@enduml
```

### 2.7 Object

```plantuml
@startuml
object "shape: Circle" as c {
  radius = 3.0
}
object "point: Point" as p {
  x = 1
  y = 2
}
c --> p : centre
@enduml
```

### 2.8 Mindmap

```plantuml
@startmindmap
* renderer
** Markdown
*** headings
*** tables
** Math
*** KaTeX
** Diagrams
*** Mermaid
*** PlantUML
@endmindmap
```

### 2.9 Work breakdown structure

```plantuml
@startwbs
* Release
** Design
*** Spec
*** Review
** Build
*** Implement
*** Test
@endwbs
```

### 2.10 Gantt

```plantuml
@startgantt
Project starts 2026-01-01
[Spec] lasts 10 days
[Review] lasts 5 days
[Review] starts at [Spec]'s end
[Implement] lasts 20 days
[Implement] starts at [Review]'s end
@endgantt
```

### 2.11 Timing

```plantuml
@startuml
robust "Web Browser" as WB
concise "Web User" as WU
WB is Idle
WU is Idle
@0
WB is Processing
@100
WB is Waiting
@300
WB is Idle
@enduml
```

### 2.12 A deliberately broken diagram

PlantUML answers this with HTTP 400. The body is a renderable error picture, so
the preview shows it at full size as if it were fine — which is exactly why the
validator reads the response body instead of trusting the image.

```plantuml
@startuml
this is not valid plantuml at all !!!
@enduml
```

### 2.13 An empty diagram renders a welcome placeholder

`@startuml` / `@enduml` with nothing between them returns HTTP 200 and the
"Welcome to PlantUML!" image. No error, but not a diagram either.

```plantuml
@startuml
@enduml
```
