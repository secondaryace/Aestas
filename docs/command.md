## 进阶-指令系统
Aestas其实将指令系统抽象为`CommandExecuter`，可以注入各种各样的指令系统到Bot中，其中的指令是自己实现的。
### AestasScript
绑定值：
```fsharp
let x = 1
```
绑定函数：
```fsharp
let f x y = + x y
let mutilineFunc x y = (
    let z = + x y
    let z = + z y
    z
)
```
函数调用：
```fsharp
f 1 2
```
前缀表达式的运算符：
```fsharp
+ 1 2
```
中缀表达式的运算符：

*中间不可以有空格*
```fsharp
1+2
```
