import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import sortByProp from 'Utilities/Array/sortByProp';
import shortenList from 'Utilities/String/shortenList';
import titleCase from 'Utilities/String/titleCase';
import AlbumReleaseSelectInputSelectedValue from './AlbumReleaseSelectInputSelectedValue';
import EnhancedSelectInput from './EnhancedSelectInput';
import styles from './AlbumReleaseSelectInput.css';

function createMapStateToProps() {
  return createSelector(
    (state, { albumReleases }) => albumReleases,
    (albumReleases) => {
      const sortedValues = albumReleases.value
        .map((albumRelease) => {
          const {
            foreignReleaseId,
            title,
            disambiguation,
            mediumCount,
            trackCount,
            country,
            format
          } = albumRelease;

          return {
            key: foreignReleaseId,
            value: `${title}` +
            `${disambiguation ? ' (' : ''}${titleCase(disambiguation)}${disambiguation ? ')' : ''}` +
            `, ${mediumCount} med, ${trackCount} tracks` +
            `${country && country.length > 0 ? `, ${shortenList(country)}` : ''}` +
            `${format ? `, [${format}]` : ''}`
          };
        })
        .sort(sortByProp('value'));

      // Tomvis fork (MUSIC-68): an album may monitor several releases (editions, MUSIC-63)
      const value = albumReleases.value
        .filter((release) => release.monitored)
        .map((release) => release.foreignReleaseId);

      return {
        values: sortedValues,
        value
      };
    }
  );
}

class AlbumReleaseSelectInputConnector extends Component {

  //
  // Listeners

  onChange = ({ name, value }) => {
    const {
      albumReleases
    } = this.props;

    // An album keeps at least one monitored release
    if (!value.length) {
      return;
    }

    const updatedReleases = albumReleases.value.map((e) => ({ ...e, monitored: value.includes(e.foreignReleaseId) }));

    this.props.onChange({ name, value: updatedReleases });
  };

  render() {
    return (
      <EnhancedSelectInput
        {...this.props}
        className={styles.albumReleaseSelect}
        selectedValueComponent={AlbumReleaseSelectInputSelectedValue}
        onChange={this.onChange}
      />
    );
  }
}

AlbumReleaseSelectInputConnector.propTypes = {
  name: PropTypes.string.isRequired,
  onChange: PropTypes.func.isRequired,
  albumReleases: PropTypes.object
};

export default connect(createMapStateToProps)(AlbumReleaseSelectInputConnector);
