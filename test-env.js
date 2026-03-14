require('dotenv').config();

console.log("API KEY FOUND:", process.env.OPENAI_API_KEY ? "YES" : "NO");
console.log("VALUE:", process.env.OPENAI_API_KEY);
