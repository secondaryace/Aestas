## Aestas内置指令
#### 语法定义
```ebnf
command     = { tuple ( ";" [ newline ] ) | newline } tuple;
tuple       = { expr "," } expr;
expr        = field | call | listLit | tuple | objectLit | pipeline | atom;
field       = expr "." identifier;
tupleLit    = { atom "^" } atom;
listLit     = "[" { expr "" } expr "]";
objectLit   = "{" { key "=" expr "" } key ":" expr "}";
call        = expr "space" atom { "space" atom  } | binOp atom atom (* sugar, make call like "ls" valid *)
pipeline    = expr "|" expr;
atom        = number | string | identifier | "(" expr ")";
```

#### 例子
```
lsdomain | map (lambda x x.name); (* list all domains *) | echo
lsdomain | filter (lambda x (eq x.name "test")) | echo
let f (lambda x y (add x y))
let g (lambda x^y (add x y))
f 1 2 | echo
g 1^2 | echo
```