# Tests
`ScoreTest.cs` is the standalone battery for the scoring engine (rules correctness
+ Monte Carlo vs closed-form probability). It was compiled and run against
`Code/Core/TennisScore.cs` during the build — 20/20 rules assertions passed and
the simulation matched the closed form at every p:

    p/point | game  | set   | match(cf) | match(sim, 100k)
     0.50   | 0.500 | 0.500 |  0.500    |  0.500
     0.53   | 0.575 | 0.705 |  0.790    |  0.789
     0.55   | 0.623 | 0.815 |  0.910    |  0.910
     0.60   | 0.736 | 0.963 |  0.996    |  0.996
     0.65   | 0.830 | 0.996 |  1.000    |  1.000
     0.75   | 0.949 | 1.000 |  1.000    |  1.000

(The harness targets classic C# for portability; the engine file compiles under
both this and s&box's modern compiler.)
