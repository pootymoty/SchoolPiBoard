import type { ReactElement } from 'react';

/**
 * Фон главной страницы: книжные полки.
 *
 * Пока это статичная отрисовка. Анимация (дрожание полок под курсором, пыль
 * и выпадающие при прокрутке книги) добавляется сюда же и больше нигде:
 * страница обращается к фону одним тегом, поэтому подменить содержимое
 * можно, не трогая саму главную.
 *
 * Как добавлять: книги уже разложены отдельными элементами с классом
 * `book`, у полок класс `shelf` — этого достаточно, чтобы навесить
 * трансформации и обработчики, не меняя разметку страницы.
 */
export function BookshelfBackground(): ReactElement {
  const shelves = [0, 1, 2, 3];

  return (
    <div className="bookshelf" aria-hidden="true">
      {shelves.map((shelf) => (
        <div className="shelf" key={shelf}>
          {BOOKS.map((book, index) => (
            <span
              key={index}
              className="book"
              style={{ height: book.height, width: book.width, backgroundColor: book.color }}
            />
          ))}
        </div>
      ))}
    </div>
  );
}

const BOOKS = [
  { height: 96, width: 18, color: '#5b6cf7' },
  { height: 82, width: 14, color: '#c9a227' },
  { height: 104, width: 22, color: '#2f6f4e' },
  { height: 76, width: 16, color: '#a34242' },
  { height: 92, width: 20, color: '#4a4a68' },
  { height: 86, width: 15, color: '#7d5ba6' },
  { height: 98, width: 19, color: '#2d6c8f' },
  { height: 80, width: 17, color: '#b5651d' },
  { height: 90, width: 21, color: '#3f7d4e' },
  { height: 74, width: 13, color: '#8a2f4a' },
];
